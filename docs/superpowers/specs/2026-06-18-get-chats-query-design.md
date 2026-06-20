# Get Chats Query — Design Spec

**Goal:** Give users a ChatGPT-style chat history list. A single `GET /me/chats` endpoint returns the authenticated user's chats — metadata only, no messages — as an offset-paginated page with a total count, pinned chats first and the rest by recency.

**Builds on:** The existing `ChatThread` aggregate (`Title`, `PinnedAt`, `IsArchived`, `IsTemporary`, `CreatedAt`, `UpdatedAt`), the `chats` table and its `(user_id, updated_at DESC, id)` index, the `GetFavoriteModels` query as the structural precedent (Query → Handler → reader interface → Dapper reader → ReadModels → Endpoint/Response/Mapper), and the observed ChatGPT conversation-list contract (`items` / `total` / `limit` / `offset`).

---

## 1. Scope

**In scope**

- `GET /me/chats` returning a single offset page of chat metadata.
- `limit` / `offset` query params with bounds, and a `total` count in the response.
- Exclude temporary chats and archived chats from the result, always.
- Pinned chats first (by `pinnedAt`), then the rest by recency (`updatedAt`).
- Per-item metadata only: id, title, pin/archive/temporary flags, timestamps.

**Out of scope**

- Returning messages, `currentMessageId`, or any conversation tree content.
- A separate archived-chats view or an `?archived=true` toggle.
- Title search / filtering by date.
- Keyset/cursor pagination (offset/limit chosen to match the reference contract).
- A `pinned_at` index (deferred; see §5).

---

## 2. API Contract

### 2.1 Endpoint

```http
GET /me/chats?limit=20&offset=0
```

- `limit`: optional, default **20**, valid range **[1, 100]**.
- `offset`: optional, default **0**, must be **>= 0**.

Out-of-range values return `400 Bad Request` via the existing `ValidationBehavior` (a `GetChatsQueryValidator` produces `Error.Validation`).

### 2.2 Success response

```http
200 OK
```

```json
{
  "items": [
    {
      "id": "6a338196-2a28-83ed-8999-e5273757f471",
      "title": "მენეჯმენტი თავი #17",
      "isPinned": false,
      "pinnedAt": null,
      "isArchived": false,
      "isTemporary": false,
      "createdAt": "2026-06-18T05:26:57.528363+00:00",
      "updatedAt": "2026-06-18T09:00:44.108485+00:00"
    }
  ],
  "total": 29,
  "limit": 20,
  "offset": 0
}
```

`total` is the count of the user's chats under the same filter (non-temporary, non-archived), independent of `limit`/`offset`, so the client can render "showing X of total" and compute page count.

`isArchived` and `isTemporary` are always `false` in this list (they are filtered out). They are kept on each item so the item shape matches the existing `ChatThreadResponse`, giving the frontend one chat shape.

---

## 3. Ordering and Filtering

**Filter** (fixed, not client-controlled):

```sql
where user_id = @UserId and is_temporary = false and is_archived = false
```

**Order:**

```sql
order by (pinned_at is null), pinned_at desc, updated_at desc, id desc
```

Pinned chats sort first by `pinned_at` (most recently pinned on top), then unpinned chats by `updated_at`. `id desc` is the final tiebreaker for a stable, deterministic order across pages.

`updatedAt` reflects conversation activity (messages/turns), not sidebar metadata changes — consistent with the chat-thread-update contract, which intentionally does not bump `UpdatedAt` on metadata edits. Recency ordering therefore tracks real conversation activity.

---

## 4. Application Flow

All new types live under `Chat.Application/Chats/Queries/GetChats/`.

### 4.1 Query

```csharp
public sealed record GetChatsQuery(int Limit, int Offset) : IQuery<ErrorOr<ChatListReadModel>>;
```

### 4.2 Validator

`GetChatsQueryValidator : AbstractValidator<GetChatsQuery>` — `Limit` in `[1, 100]`, `Offset >= 0`. Auto-discovered by `AddValidatorsFromAssembly` and run by `ValidationBehavior`.

### 4.3 Handler

`GetChatsHandler` (`internal sealed`), mirroring `GetFavoriteModelsHandler`:

1. `UserId.Create(userContext.UserId)`; return its errors if invalid.
2. `await reader.GetAsync(userId, query.Limit, query.Offset, cancellationToken)`.
3. Return the `ChatListReadModel`.

### 4.4 Reader interface

```csharp
public interface IChatListReader
{
    Task<ChatListReadModel> GetAsync(UserId userId, int limit, int offset, CancellationToken cancellationToken);
}
```

### 4.5 Read models

```csharp
public sealed record ChatListReadModel(
    IReadOnlyList<ChatSummaryReadModel> Items,
    int Total,
    int Limit,
    int Offset);

public sealed record ChatSummaryReadModel(
    Guid Id,
    string Title,
    bool IsPinned,
    DateTimeOffset? PinnedAt,
    bool IsArchived,
    bool IsTemporary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
```

Dedicated query-side read models — not the command-side `ChatThreadResult` — keeping the existing Result/ReadModel CQRS split intact (the same way `GetFavoriteModels` has its own `FavoriteLlmModelReadModel`).

---

## 5. Persistence

No schema change and no new migration. The query reads existing `chats` columns.

The `(user_id, updated_at DESC, id)` index is not fully usable by the pinned-first sort because `pinned_at` leads the `ORDER BY`. For realistic per-user chat counts this is fine: Postgres filters by `user_id` and sorts a small result set. Do **not** add a `pinned_at` index now (YAGNI); revisit only if a single user accumulates thousands of non-archived chats.

---

## 6. Infrastructure (Reader)

`Chat.Infrastructure/Chats/Readers/ChatListReader.cs`, following `FavoriteModelsReader`: `NpgsqlDataSource`, raw SQL, a private `Row` record, registered in `Chat.Infrastructure/DependencyInjection.cs`.

Count and page are fetched in one round-trip via `QueryMultipleAsync`:

```sql
select count(*) from chats
where user_id = @UserId and is_temporary = false and is_archived = false;

select id           as "Id",
       title        as "Title",
       pinned_at    as "PinnedAt",
       is_archived  as "IsArchived",
       is_temporary as "IsTemporary",
       created_at   as "CreatedAt",
       updated_at   as "UpdatedAt"
from chats
where user_id = @UserId and is_temporary = false and is_archived = false
order by (pinned_at is null), pinned_at desc, updated_at desc, id desc
limit @Limit offset @Offset;
```

`IsPinned` is derived in the mapping as `PinnedAt is not null`. Returns `new ChatListReadModel(items, total, limit, offset)`.

`QueryMultipleAsync` is preferred over a window-function `count(*) over()` so that an `offset` past the end still returns the correct `total` (a window count returns no rows when the page is empty).

---

## 7. API Implementation

`Chat.Api/Endpoints/Chats/GetChats/` with `Endpoint.cs`, `Response.cs`, `ResponseMapper.cs`, following `GetFavoriteModels`.

The endpoint needs query params, so it uses `Endpoint<Request>` (not `EndpointWithoutRequest`) with a request record whose `Limit`/`Offset` are `[QueryParam]`-bound with defaults:

```csharp
internal sealed record Request(
    [property: QueryParam] int Limit = 20,
    [property: QueryParam] int Offset = 0);
```

```csharp
Get("/me/chats");
Version(1);
Options(b => b.WithName(RouteName)); // "Chat.Chats.List"
```

Description advertises `200 OK` with the list `Response`, `400 Bad Request`, and `401 Unauthorized`, tagged `CustomTags.Chats`. The handler maps the request into `GetChatsQuery`, sends it through `ISender`, returns `CustomResults.Problem(result)` on error, and otherwise `ResponseMapper.ToResponse(result.Value)`.

Response types live under the endpoint folder (mirroring `GetFavoriteModels`):

```csharp
internal sealed record Response(
    IReadOnlyList<ChatListItemResponse> Items,
    int Total,
    int Limit,
    int Offset);

internal sealed record ChatListItemResponse(
    Guid Id,
    string Title,
    bool IsPinned,
    DateTimeOffset? PinnedAt,
    bool IsArchived,
    bool IsTemporary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
```

Use FastEndpoints and the existing `Mediator` package. Do not use controllers or MediatR.

---

## 8. Data Flow

```text
GET /me/chats?limit=20&offset=0
  -> FastEndpoints endpoint binds Limit/Offset query params
  -> GetChatsQuery(limit, offset)
  -> ValidationBehavior enforces bounds (400 on violation)
  -> GetChatsHandler resolves UserId from IUserContext
  -> IChatListReader.GetAsync(userId, limit, offset)
  -> ChatListReader runs count + page (QueryMultipleAsync)
  -> ChatListReadModel(items, total, limit, offset)
  -> 200 OK with list Response
```

---

## 9. Error Handling

- Invalid `limit`/`offset` (out of range): `Error.Validation` → `400 Bad Request`.
- Unauthenticated / invalid user id: handled as today via `IUserContext` + `UserId.Create` errors.
- No chats: `200 OK` with `items: []` and `total: 0`.
- `offset` past the end: `200 OK` with `items: []` and the real `total`.

All errors flow through existing `ErrorOr` and `CustomResults.Problem` handling.

---

## 10. Testing

Per project instruction, tests are added only if explicitly requested. If requested, focus on:

- Handler: invalid user id surfaces errors; valid path delegates to the reader and returns its result.
- Reader (integration): filter excludes temporary and archived; pinned-first then recency ordering; `total` independent of `limit`/`offset`; empty page past the end still returns the correct `total`.
- Validator: rejects `limit` outside `[1, 100]` and negative `offset`; accepts defaults.

---

## 11. Alternatives Considered

### Recommended: offset / limit / total

Matches the ChatGPT reference contract exactly and lets the UI show total count and page numbers. Cost is a `COUNT(*)` per call and slower deep offsets — acceptable for a per-user chat sidebar.

### Keyset / cursor pagination

The `(user_id, updated_at DESC, id)` index is shaped for this, and it stays fast at any depth without skip/duplicate anomalies. Rejected for now because it returns no total count and diverges from the chosen reference contract. Reconsider if chat lists grow large enough that deep offset scans hurt.

---

## 12. Implementation Notes

- Reuse the `GetFavoriteModels` slice as the template for every layer.
- Keep filtering server-fixed (non-temporary, non-archived); do not expose it as a client param in this spec.
- Derive `IsPinned` from `PinnedAt`; do not select a separate column.
- Register `ChatListReader` next to `FavoriteModelsReader` in `Chat.Infrastructure/DependencyInjection.cs`.
- Keep `id desc` as the final sort key for deterministic paging.
