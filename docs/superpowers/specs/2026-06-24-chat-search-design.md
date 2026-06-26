# Chat Search - Design Spec

**Goal:** Add authenticated chat-history search to the backend. Search returns chat-level results ranked by title and message-content relevance, with bounded message snippets explaining why each chat matched. Indexing is asynchronous, durable, and debounced so active LLM conversations do not cause excessive Elasticsearch writes.

**Builds on:** Existing Chat.Api/FastEndpoints request style, `Mediator` query/command handlers, Dapper query readers, MassTransit with EF outbox, RabbitMQ, PostgreSQL, Aspire AppHost resources, and the current chat aggregate/message persistence model.

---

## 1. Scope

**In scope**

- `GET /me/chats/search` for authenticated chat search.
- Query text plus archive-state filtering.
- Chat-level search results with `matchCount` and up to 3 plain snippets from the best matching messages.
- Elasticsearch as the search engine.
- Dedicated `Chat.SearchWorker` service.
- Durable `ChatSearchIndexRequested` messages published through MassTransit + EF outbox.
- PostgreSQL debounce state keyed by chat id.
- Whole-chat snapshot reindexing after about 60 seconds of quiet time.
- Manual backfill path for existing non-temporary chats.
- PostgreSQL validation/enrichment of search hits before returning them.

**Out of scope**

- Searching temporary chats.
- Public/shared-chat search.
- PostgreSQL fallback search when Elasticsearch is unavailable.
- Date/model/role/pinned filters.
- Returning all matched message ids.
- Highlight range/tag metadata in the v1 response.
- Incremental per-message indexing optimization.

---

## 2. Product Behavior

Search is a separate operation from normal chat listing. If the user opens the search UI but has not typed a query, the frontend should not call the backend search endpoint. A blank or whitespace-only query sent to the backend returns `400 Bad Request`.

Search results are chat-level. A chat appears at most once in the response even when multiple messages match. The response includes a match count and up to 3 snippets so the user can tell why the chat matched.

Archived chats are controlled by the request. Searching the normal chat view uses `isArchived=false`; searching the archive view uses `isArchived=true`. Temporary chats are never indexed and never returned.

Search freshness is intentionally eventual. New or changed chats should usually become searchable about 60 seconds after the last persisted chat activity. This trades small staleness for significantly fewer indexing operations during active conversations.

---

## 3. Architecture

Use durable debounced snapshot indexing:

1. Chat mutations persist through existing command/turn flows.
2. Relevant mutations publish `ChatSearchIndexRequested` through `IMessageBus`.
3. MassTransit EF outbox keeps the indexing request transactionally tied to the chat database write.
4. `Chat.SearchWorker` consumes the message and upserts a debounce row in PostgreSQL.
5. A background processor claims due debounce rows and reindexes the whole chat snapshot into Elasticsearch.
6. Search queries Elasticsearch for ranked candidates.
7. The API validates and enriches candidate chats from PostgreSQL before responding.

This keeps request handlers free of Elasticsearch writes, avoids dual-write problems, and gives the search worker independent failure/retry behavior.

---

## 4. Search Engine

Use Elasticsearch for v1.

Before implementation, verify the current Aspire and .NET package support for Elasticsearch. Prefer first-class Aspire hosting/client packages if they are available for the project’s Aspire version. If not, add Elasticsearch to AppHost as an explicit container resource and pass configuration to Chat.Api and Chat.SearchWorker.

The application should hide Elasticsearch client details behind application/infrastructure abstractions so query handlers and indexing services do not depend directly on client-specific types.

---

## 5. Index Model

Use message-level Elasticsearch documents grouped into chat-level API results.

Each searchable document represents one completed user or assistant message:

```text
id: "{chatId}:{messageId}"
chatId
messageId
userId
chatTitle
role
content
messageCreatedAt
chatUpdatedAt
isArchived
```

Document rules:

- Include completed user messages.
- Include completed assistant messages.
- Exclude generating assistant messages.
- Exclude failed assistant messages as searchable content.
- Exclude temporary chats entirely.
- Do not create public/shared search documents.

`chatTitle` is duplicated onto message documents. Whole-chat snapshot reindexing keeps duplicated metadata simple: when title/archive/content changes, delete the chat’s existing documents and write the current snapshot.

Ranking rules:

- Search `chatTitle` and `content`.
- Give title matches a moderate boost.
- Allow modest fuzziness for title matching.
- Keep message-content matching tokenized and predictable; do not enable broad fuzziness for content in v1.
- Group/collapse results by `chatId`.
- Rank chats by a combination of best document score and match count, so multiple strong content matches can outrank a weak title-only match.

---

## 6. Indexing Triggers

Publish `ChatSearchIndexRequested` for chat mutations that can affect search results or search eligibility:

- Chat created.
- User message added.
- Assistant message completed.
- Chat renamed.
- Chat archived or unarchived.
- Branch/edit/regenerate flows when they add or complete searchable messages.
- Chat deletion or cleanup paths, when applicable, so indexed content can be removed or skipped on reindex.

Pin/unpin does not need to affect the search index unless search results later become pinned-first. In v1, search ranks by relevance, so pin state is loaded from PostgreSQL during response enrichment rather than used for Elasticsearch ranking.

The event should carry enough routing metadata for the search worker to update debounce state without loading the aggregate:

```csharp
public sealed record ChatSearchIndexRequested(
    Guid ChatId,
    Guid UserId,
    string Reason,
    DateTimeOffset OccurredAt);
```

The exact contract can live in the Chat application/infrastructure boundary unless another service needs to publish or consume it. Use the existing `Mediator` package for in-process handlers and MassTransit for durable cross-process delivery; do not introduce MediatR.

---

## 7. Debounce State

Store debounce state in the Chat PostgreSQL database.

Proposed table:

```text
chat_search_index_jobs
  chat_id uuid primary key
  user_id uuid not null
  index_after timestamptz not null
  last_requested_at timestamptz not null
  status text not null
  attempt_count integer not null
  last_error text null
  locked_until timestamptz null
  created_at timestamptz not null
  updated_at timestamptz not null
```

Status values:

- `pending`
- `processing`
- `failed`

On `ChatSearchIndexRequested`, the consumer upserts by `chat_id`:

- `index_after = max(existing.index_after, occurred_at + debounceDelay)`
- `last_requested_at = max(existing.last_requested_at, occurred_at)`
- `status = pending`
- preserve useful failure metadata only if it still helps diagnostics

The debounce delay should default to about 60 seconds and be configurable.

The due-job processor claims rows with PostgreSQL row locking, for example `FOR UPDATE SKIP LOCKED`, so multiple worker instances can run safely. If indexing succeeds, mark the row complete by deleting it or setting it to a completed state. Prefer deleting completed rows unless operational history is needed.

If Elasticsearch is unavailable or indexing fails, keep the row pending/failed for retry with backoff and record `last_error`.

---

## 8. Reindex Algorithm

When a debounce row is due:

1. Load the authoritative chat snapshot from PostgreSQL for `chat_id` and `user_id`.
2. Delete all Elasticsearch documents for that `chatId`.
3. If the chat no longer exists, is temporary, or otherwise should not be searchable, stop after deletion.
4. Build documents for completed user and assistant messages.
5. Bulk index the documents.
6. Mark the job complete.

Whole-chat replacement is intentionally chosen for v1. It is idempotent, repairs stale duplicated metadata, and simplifies deletes, archive changes, title changes, branching, editing, and regeneration. Incremental indexing can be added later if measured indexing cost requires it.

---

## 9. Search API

### 9.1 Endpoint

```http
GET /me/chats/search?query=memory%20bug&isArchived=false&limit=20&offset=0
```

Use FastEndpoints and the existing query-handler style:

- Endpoint request model under `Chat.Api/Endpoints/Chats/SearchChats`.
- `SearchChatsQuery : IQuery<ErrorOr<ChatSearchReadModel>>`.
- Validator for query, limit, and offset.
- Handler resolves authenticated `UserId`, calls a search reader/service, and returns read models.

Validation:

- `query` is required and must contain non-whitespace text.
- `limit` defaults to `ChatLimits.DefaultQueryLimit` and uses the same maximum as `GetChatsQueryValidator`.
- `offset` defaults to `0` and must be non-negative.
- `isArchived` is required or defaults to `false`, matching existing endpoint conventions.

### 9.2 Response

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
          "role": "assistant",
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

Return up to 3 snippets per chat. Snippets are plain bounded text in v1. The response can later add highlight metadata without changing the basic result shape.

---

## 10. Consistency And Authorization

PostgreSQL is the authority. Elasticsearch provides candidate ranking and snippets only.

Search flow:

1. Query Elasticsearch for candidate chat groups owned by the authenticated `userId`.
2. Request more candidates than the requested page size because PostgreSQL validation may discard stale hits.
3. Load candidate chats from PostgreSQL for the authenticated user.
4. Filter by `isArchived` and `isTemporary = false`.
5. Drop missing/deleted/wrong-state candidates.
6. Preserve Elasticsearch relevance order for the validated results.
7. Return enriched metadata from PostgreSQL plus snippets/match counts from Elasticsearch.

This prevents stale index metadata from authorizing or exposing chats. Archive state is also checked in PostgreSQL, so a debounced archive change cannot remain visible indefinitely through stale index data.

If Elasticsearch is unavailable, return a service-unavailable style error from the search endpoint. Do not fall back to PostgreSQL title/content search in v1.

---

## 11. Backfill

Provide a manual backfill path that queues all existing non-temporary chats for indexing.

The backfill should:

- Be explicitly triggered, not automatic at production startup.
- Page through non-temporary chats from PostgreSQL.
- Upsert debounce rows or publish `ChatSearchIndexRequested` messages.
- Support throttling/batching.
- Be safe to rerun.

This gives the release a path to make historical chats searchable and gives operators a repair path after index mapping changes.

---

## 12. Aspire And Deployment

Add a dedicated `Chat.SearchWorker` project and register it in `Nova.AppHost`.

Expected resources:

- Chat DB connection.
- RabbitMQ connection.
- Elasticsearch endpoint/configuration.

`Chat.Api` needs Elasticsearch configuration for querying. `Chat.SearchWorker` needs Elasticsearch configuration for indexing. If a first-class Aspire Elasticsearch integration exists for the current package set, use it. Otherwise, define an Elasticsearch container resource explicitly and wire endpoint environment variables/configuration in AppHost.

Keep MassTransit version unchanged. The current pin is intentional.

---

## 13. Error Handling And Retries

Indexing failures:

- Record failure details on the debounce row.
- Retry with backoff.
- Do not block chat writes.
- Do not publish partial user-visible success claims from the indexer.

Search failures:

- If Elasticsearch is unavailable, return service unavailable.
- If PostgreSQL validation fails unexpectedly, return the normal application error path.
- Empty successful matches return an empty result set, not an error.

Index replacement should be designed to avoid permanent duplicate/stale documents. Prefer deterministic document ids and delete-by-chat before bulk indexing.

---

## 14. Testing Strategy

Ask before adding or expanding tests, per project instruction.

When implementation is approved, the risk areas that should be covered are:

- Query validation: blank query, limit/offset bounds.
- Search handler authorization/validation against PostgreSQL.
- Debounce upsert behavior.
- Reindex snapshot rules: temporary chats skipped, generating/failed assistant messages skipped, completed user/assistant messages indexed.
- Archive filtering.
- Backfill batching/idempotence.

---

## 15. Open Implementation Checks

- Verify current Elasticsearch package choices for .NET 10/Aspire 13.x before editing project files.
- Confirm whether to use a first-class Aspire Elasticsearch integration or an explicit container resource.
- Decide the exact Elasticsearch client package and mapping syntax after verification.
- Decide whether completed debounce rows are deleted or retained briefly for observability.
- Decide the exact service-unavailable error representation using the project’s existing `ErrorOr`/problem-details conventions.
