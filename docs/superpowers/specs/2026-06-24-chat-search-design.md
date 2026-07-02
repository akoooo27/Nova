# Chat Search - Design Spec

**Goal:** Add authenticated chat-history search to the backend. Search returns chat-level results ranked by title and message-content relevance, with bounded message snippets explaining why each chat matched. Search runs directly against PostgreSQL full-text search, so results are transactionally fresh and no indexing pipeline is required.

**Builds on:** Existing Chat.Api/FastEndpoints request style, `Mediator` query/command handlers, Dapper query readers, PostgreSQL, and the current chat aggregate/message persistence model.

> **Revision note (2026-07-02):** This spec replaces the earlier Elasticsearch-based design. The previous design required a second data store and, as a consequence, an outbox message contract, a consumer, a debounce job table, a polling claim loop, whole-chat snapshot reindexing, a manual backfill path, and a read-time PostgreSQL validation layer — plus 60 seconds of accepted staleness. All of that machinery existed only to synchronize Elasticsearch with PostgreSQL. Searching PostgreSQL directly removes the second store and everything downstream of it, while meeting every v1 product requirement. The read-side abstraction (`IChatSearchReader`) is the seam for swapping in an external engine later if search ever outgrows PostgreSQL.

---

## 1. Scope

**In scope**

- `GET /me/chats/search` for authenticated chat search.
- Query text plus archive-state filtering.
- Chat-level search results with `matchCount` and up to 3 plain snippets from the best matching messages.
- PostgreSQL full-text search (`tsvector`/`tsquery`, GIN index) as the search engine.
- A database-generated `search_vector` column on `chat_messages`, invisible to the domain model and EF Core.
- Exact result totals and correct offset pagination.
- Immediate searchability: a committed message is searchable on the next request.

**Out of scope**

- Searching temporary chats.
- Public/shared-chat search.
- Date/model/role/pinned filters.
- Highlight range/tag metadata in the v1 response (snippets are plain text).
- Semantic/vector search.
- Searching `Stopped` (partially generated) assistant messages. Only `Completed` messages are searchable in v1; widening the status filter later is a one-line query change.
- Restricting search to the active conversation branch. All message-tree branches are searched in v1 (the previous design had the same behavior).
- Elasticsearch, search workers, indexing queues, and backfill tooling.

---

## 2. Product Behavior

Search is a separate operation from normal chat listing. If the user opens the search UI but has not typed a query, the frontend should not call the backend search endpoint. A blank or whitespace-only query sent to the backend returns `400 Bad Request`.

Search results are chat-level. A chat appears at most once in the response even when multiple messages match. The response includes a match count and up to 3 snippets so the user can tell why the chat matched. A chat whose title matches but whose messages do not is still returned (with zero message matches and no snippets).

Archived chats are controlled by the request. Searching the normal chat view uses `isArchived=false`; searching the archive view uses `isArchived=true`. Temporary chats are never returned.

Search freshness is immediate. The search index is a generated column maintained by PostgreSQL in the same transaction as the message write, so a chat is searchable the moment its write commits. There is no debounce window and no eventual-consistency caveat.

---

## 3. Architecture

Single-store, synchronous read path:

1. Chat mutations persist through existing command/turn flows — **no changes to any write path**.
2. PostgreSQL maintains `chat_messages.search_vector` automatically as a stored generated column.
3. `GET /me/chats/search` binds to `SearchChatsQuery`, handled by a `Mediator` query handler.
4. The handler resolves the authenticated `UserId` and calls `IChatSearchReader`.
5. The Dapper-based reader executes one round trip (count statement + page statement) against PostgreSQL and returns read models.

There is no message contract, no consumer, no worker, no job table, and no backfill: existing rows become searchable as soon as the migration creating the column and index is applied.

### Domain isolation (hard constraint)

The domain model must not know about search. `search_vector`:

- is **not** a property on `ChatMessage`,
- is **not** mapped in `ChatDbContext` or any `IEntityTypeConfiguration`,
- is created by raw SQL in a hand-written EF migration (repo precedent: `20260614161000_ChatPinArchive.cs`).

Because the column is absent from the EF model and snapshot, EF migrations will never generate operations against it, and EF never reads or writes it. The only code aware of the column is the Dapper reader.

---

## 4. Search Index

One stored generated column plus one GIN index on `chat_messages`:

```sql
alter table chat_messages
    add column search_vector tsvector
    generated always as (to_tsvector('simple', coalesce(content, ''))) stored;

create index ix_chat_messages_search_vector on chat_messages using gin (search_vector);
```

Decisions:

- **`simple` text search configuration.** Chat content is multilingual and full of code identifiers; language stemming (`english`) would produce surprising matches and misses. `simple` lowercases and tokenizes without stemming, which is predictable. The same configuration **must** be used everywhere a `tsvector`/`tsquery` is built (column expression, title match, `ts_headline`), otherwise queries stop matching the indexed expression.
- **Stored generated column** rather than an expression index, so ranking (`ts_rank`) and matching read the precomputed vector instead of re-parsing up to 32 KB of content per row.
- **`chats.title` is matched at query time** with `to_tsvector('simple', title)` and no index. Titles are short and per-user chat counts are small; a stored vector for titles is not warranted. An expression index can be added later without any code change.
- `content` is `varchar(32768)`, well under `tsvector` limits; `coalesce` handles messages with `NULL` content (they produce an empty vector and never match).

Searchable rows are constrained at query time, not index time:

- `chats.user_id = @UserId` — authorization is part of the query itself.
- `chats.is_temporary = false`.
- `chats.is_archived = @IsArchived`.
- `chat_messages.status = 'Completed'` — excludes `Generating`, `Failed`, and `Stopped` messages.

---

## 5. Query And Ranking

Parse user input with `websearch_to_tsquery('simple', @Query)`. It never throws on malformed input and supports quoted phrases, `or`, and `-exclusion` for free. Input that yields an empty `tsquery` (e.g. only punctuation) simply matches nothing.

Chat-level score:

```text
score = 2.0 * ts_rank(title_vector, query)          -- 0 when the title does not match
      + best_message_rank                            -- max ts_rank over the chat's matching messages
      + 0.05 * least(match_count, 20)                -- several strong content matches can outrank a weak title match
```

Ordering: `score desc, updated_at desc, id desc`. The formula is a starting point and is confined to one SQL expression; tuning it later touches nothing else.

Results are grouped per chat in SQL (`group by chat_id` for stats, `left join lateral ... limit 3` for snippets), so pagination and totals operate on chats, not messages. `total` is an exact count of matching chats and `limit`/`offset` paginate deterministically.

Snippets: up to 3 per chat, best-ranked messages first, generated with:

```sql
ts_headline('simple', content, query, 'MaxFragments=1, MaxWords=18, MinWords=6, StartSel="", StopSel=""')
```

Empty `StartSel`/`StopSel` produce plain bounded text with no markup. The response shape can add highlight metadata later without breaking.

---

## 6. Search API

### 6.1 Endpoint

```http
GET /me/chats/search?query=memory%20bug&isArchived=false&limit=20&offset=0
```

Follows the existing `GetChats` endpoint conventions:

- FastEndpoints `Request` record bound from query parameters, defined in `Endpoint.cs`.
- `SearchChatsQuery : IQuery<ErrorOr<ChatSearchReadModel>>` dispatched through `Mediator`.
- FluentValidation validator runs in the existing `ValidationBehavior` pipeline.
- Handler resolves the authenticated `UserId`, trims the query text, and calls `IChatSearchReader`.

Validation:

- `query` is required, must contain non-whitespace text, and is capped at `ChatLimits.MaxSearchQueryLength` (256).
- `limit` defaults to `ChatLimits.DefaultQueryLimit` and is bounded by `ChatLimits.MinQueryLimit`/`ChatLimits.MaxQueryLimit`, same as `GetChatsQueryValidator`.
- `offset` defaults to `0` and must be non-negative.
- `isArchived` defaults to `false`, matching existing endpoint conventions.

### 6.2 Response

```json
{
  "items": [
    {
      "id": "6a338196-2a28-83ed-8999-e5273757f471",
      "title": "Planning Nova search",
      "isPinned": false,
      "pinnedAt": null,
      "isArchived": false,
      "createdAt": "2026-06-18T05:26:57.528363+00:00",
      "updatedAt": "2026-06-18T09:00:44.108485+00:00",
      "matchCount": 7,
      "snippets": [
        {
          "messageId": "018f7e9e-5f95-7b51-a9af-6d0dd0f37de0",
          "role": "Assistant",
          "text": "bounded plain snippet..."
        }
      ]
    }
  ],
  "total": 12,
  "limit": 20,
  "offset": 0
}
```

`total` is exact. `role` uses the stored enum names (`User`/`Assistant`). A title-only match has `matchCount: 0` and `snippets: []`.

---

## 7. Consistency And Authorization

There is one store. The search query filters by `user_id`, `is_temporary`, `is_archived`, and message `status` inside the same statement that ranks and paginates, against committed data. There is no stale-index window, no cross-store validation step, no over-fetch multiplier, and no way for a rename, archive toggle, or delete to leave search out of sync.

---

## 8. Error Handling

- Blank/oversized query, bad limit/offset: `400` via the standard validation pipeline.
- Unauthenticated: `401` via existing auth conventions.
- Database failure: the standard application error path — search has no special availability story separate from the rest of the API, because it has no extra infrastructure to become unavailable.
- No matches: `200` with `items: []` and `total: 0`.

---

## 9. Testing Strategy

Tests are approved for this feature.

- Validator: blank/whitespace query, over-length query, limit/offset bounds.
- Handler: passes trimmed query and authenticated user id to the reader; returns reader result; returns error and skips the reader when the user id is missing.
- Reader SQL is exercised by build verification and the manual checks in the plan's final task (the repo has no infrastructure test project).

---

## 10. Future Evolution

- **External search engine:** `IChatSearchReader` is the seam. If search outgrows PostgreSQL (cross-user scale, semantic search, heavy relevance tuning), implement the same interface against Elasticsearch/pgvector and build the indexing pipeline then, justified by measured need. The API contract does not change.
- **Fuzzier title matching:** add `pg_trgm` similarity on `chats.title` as an additional score term.
- **Stopped messages:** include partially generated content by widening the status filter.
- **Active-branch-only search:** restrict message matching to the active conversation path if searching abandoned branches proves confusing.
- **Highlight metadata:** switch `ts_headline` delimiters to sentinel markers and emit ranges alongside `text`.
