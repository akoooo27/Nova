# Chat Thread Update — Design Spec

**Goal:** Give users one ChatGPT-style update surface for chat thread metadata. Rename, pin, unpin, archive, unarchive, hide, and unhide all flow through a single sparse `PATCH /chats/{chatId}` endpoint. The request contains only the fields being changed, and the response returns the full updated chat metadata state.

**Builds on:** The existing `ChatThread` aggregate, which already stores `Title`, `PinnedAt`, `IsArchived`, and `IsTemporary`, and on the observed ChatGPT behavior where metadata changes are sparse `PATCH` requests to the conversation resource.

---

## 1. Scope

**In scope**

- `PATCH /chats/{chatId}` as the single metadata update endpoint.
- Sparse request semantics: omitted fields are unchanged.
- Full metadata response on success with `200 OK`.
- Rename through `title`.
- Pin/unpin through `isPinned`.
- Archive/unarchive through `isArchived`.
- Hide/unhide through `isVisible`.
- Hidden-chat deletion eligibility after 30 days, driven by a timestamp.

**Out of scope**

- Separate `/rename`, `/pin`, `/archive`, or `/hide` action endpoints.
- Mixing archive with visibility. Archived chats are not deleted just because they are archived.
- Changing `IsTemporary` after creation.
- Implementing a chat history listing endpoint in this spec.

---

## 2. API Contract

### 2.1 Endpoint

```http
PATCH /chats/{chatId}
```

Successful updates return:

```http
200 OK
```

with the full thread metadata response:

```json
{
  "id": "6a2ff269-e134-83ed-aee8-8a59dc544dc7",
  "title": "SSs",
  "isPinned": true,
  "pinnedAt": "2026-06-15T09:30:00+00:00",
  "isArchived": false,
  "isVisible": true,
  "hiddenAt": null,
  "isTemporary": false,
  "createdAt": "2026-06-15T09:00:00+00:00",
  "updatedAt": "2026-06-15T09:00:00+00:00"
}
```

### 2.2 Sparse request examples

Rename only:

```json
{ "title": "SSs" }
```

Pin only:

```json
{ "isPinned": true }
```

Unpin only:

```json
{ "isPinned": false }
```

Archive only:

```json
{ "isArchived": true }
```

Hide only:

```json
{ "isVisible": false }
```

Multi-field patches are allowed when the user action changes multiple independent fields:

```json
{
  "title": "Project planning",
  "isPinned": true
}
```

### 2.3 Presence and null semantics

Omitted means unchanged. Explicit `null` is invalid for all mutable fields in this contract:

```json
{ "title": null }
```

returns `400 Bad Request`, not "no change".

This matters because normal C# nullable DTOs cannot always distinguish "property omitted" from "property present with null." The implementation should either:

- use an explicit optional-field wrapper with JSON presence tracking, or
- parse the request body as JSON in the endpoint and map only present properties into the command.

The command should receive presence-aware values so the handler never guesses whether a caller intended to update a field.

Unknown properties should be rejected with `400 Bad Request`. This keeps the patch contract tight and avoids silently ignoring client typos.

---

## 3. Domain Model

### 3.1 Existing fields

Keep the existing model:

- `ChatTitle Title`
- `DateTimeOffset? PinnedAt`
- `bool IsArchived`
- `bool IsPinned => PinnedAt is not null`
- `bool IsTemporary`

### 3.2 New visibility fields

Add:

```csharp
public bool IsVisible { get; private set; }
public DateTimeOffset? HiddenAt { get; private set; }
```

Default visibility for existing and newly created chats is `true`.

`HiddenAt` is required for deletion eligibility. A boolean alone cannot answer "has this chat been hidden for 30 days?"

### 3.3 Boolean value objects

Do not introduce value object wrappers for `IsArchived`, `IsVisible`, or the patch booleans.

The model catalog uses `ModelCapabilities` because several related booleans form a named domain concept. These chat flags are independent state bits whose behavior lives in aggregate methods:

- `Pin(DateTimeOffset pinnedAt)`
- `Unpin()`
- `Archive()`
- `Unarchive()`
- `Hide(DateTimeOffset hiddenAt)`
- `Show()`

Plain booleans keep EF mapping, request mapping, and response mapping simple without losing domain behavior.

### 3.4 Aggregate methods

Add or keep aggregate methods that express transitions:

```csharp
public void Rename(ChatTitle title);
public void Pin(DateTimeOffset pinnedAt);
public void Unpin();
public void Archive();
public void Unarchive();
public void Hide(DateTimeOffset hiddenAt);
public void Show();
```

`Rename` updates `Title` only. Metadata updates do not rewrite `UpdatedAt`; that timestamp continues to mean conversation activity from messages/turns, not sidebar metadata activity.

`Pin` keeps the existing idempotent behavior: if already pinned, preserve the original `PinnedAt`.

`Hide` sets `IsVisible = false` and sets `HiddenAt` only when transitioning from visible to hidden. Repeating a hide operation does not reset the deletion clock.

`Show` sets `IsVisible = true` and clears `HiddenAt`.

Archive and visibility are independent:

- `Archive()` does not hide.
- `Unarchive()` does not show.
- `Hide()` does not archive.
- `Show()` does not unarchive.

---

## 4. Application Flow

### 4.1 Command

Add `UpdateChatThreadCommand` under `Chat.Application/Chats/Commands/UpdateChatThread/`.

The command carries:

- `Guid ChatId`
- optional-present `Title`
- optional-present `IsPinned`
- optional-present `IsArchived`
- optional-present `IsVisible`

It returns `ErrorOr<ChatThreadResult>`.

### 4.2 Handler

The handler:

1. Creates `UserId` from `IUserContext.UserId`.
2. Creates `ChatId` from the route id.
3. Loads the chat through `IChatRepository.GetByIdAsync(chatId, userId, ct)`.
4. Returns `ChatOperationErrors.ChatNotFound(chatId)` when missing.
5. Validates and applies only present fields.
6. Saves through `IUnitOfWork.SaveChangesAsync(ct)`.
7. Returns a full metadata result.

Validation rules:

- `title` must pass `ChatTitle.Create`.
- At least one mutable field must be present. Empty `{}` returns validation error.
- Explicit `null` returns validation error.
- Visibility updates are allowed for both normal and archived chats.
- `IsTemporary` is not patchable.

The handler should use `IDateTimeProvider.UtcNow` once per request for metadata timestamps such as `PinnedAt` and `HiddenAt`.

### 4.3 Result

Add a chat metadata result/response pair, for example:

```csharp
public sealed record ChatThreadResult(
    Guid Id,
    string Title,
    bool IsPinned,
    DateTimeOffset? PinnedAt,
    bool IsArchived,
    bool IsVisible,
    DateTimeOffset? HiddenAt,
    bool IsTemporary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
```

This can later be reused by history/read endpoints when those are introduced.

---

## 5. Persistence

Update `ChatThreadConfiguration`:

```csharp
builder.Property(x => x.IsVisible)
    .IsRequired();

builder.Property(x => x.HiddenAt);
```

Add one EF migration:

- `is_visible boolean not null default true`
- `hidden_at timestamp with time zone null`

No new migration is needed for `Title`, `PinnedAt`, or `IsArchived`; those already exist.

An index is useful for cleanup:

```sql
CREATE INDEX ix_chats_hidden_cleanup
ON chats (hidden_at)
WHERE is_visible = false AND hidden_at IS NOT NULL;
```

This index is specific to the deletion policy and does not affect archive behavior.

---

## 6. Hidden Chat Cleanup

Hidden-chat cleanup is separate from archived-chat behavior.

The repository should expose a targeted delete method such as:

```csharp
Task<int> DeleteHiddenChatsAsync(DateTimeOffset hiddenBefore, CancellationToken cancellationToken = default);
```

The implementation deletes only:

```csharp
chat.IsVisible == false && chat.HiddenAt < hiddenBefore
```

It must not filter on `IsArchived`, and it must not delete visible archived chats.

The retention window defaults to 30 days. The cleanup worker/job can run on the same scheduling infrastructure as other chat cleanup work, but the predicate remains separate so the policy is explicit:

- temporary-chat cleanup deletes expired temporary chats according to the temporary-chat policy.
- hidden-chat cleanup deletes hidden chats whose `HiddenAt` is older than the retention window.
- archived chats are retained indefinitely unless also hidden by an explicit `isVisible: false` patch.

---

## 7. API Implementation

Use FastEndpoints:

```csharp
Patch("/chats/{chatId}");
Version(1);
```

Description should advertise:

- `200 OK` with `ChatThreadResponse`
- `400 Bad Request`
- `401 Unauthorized`
- `404 Not Found`
- `409 Conflict` if future state conflicts are added

The endpoint maps route id plus parsed patch fields into `UpdateChatThreadCommand`, sends it through `ISender`, and returns `CustomResults.Problem(result)` on errors.

Do not use ASP.NET Core controllers.

Do not introduce MediatR. Use the existing `Mediator` package APIs.

---

## 8. Data Flow

```text
PATCH /chats/{chatId}
  -> FastEndpoints endpoint parses sparse JSON and validates patch shape
  -> UpdateChatThreadCommand(chatId, present fields only)
  -> UpdateChatThreadHandler loads ChatThread for current UserId
  -> aggregate methods apply requested transitions
  -> SaveChangesAsync
  -> ChatThreadResult
  -> 200 OK with full ChatThreadResponse
```

Examples:

```text
{ "title": "SSs" }
  -> Rename only
  -> pin/archive/visibility unchanged
```

```text
{ "isArchived": true }
  -> Archive only
  -> IsVisible unchanged
  -> no deletion timer started
```

```text
{ "isVisible": false }
  -> Hide only
  -> HiddenAt set if this is the visible -> hidden transition
  -> eligible for cleanup after retention window
```

---

## 9. Error Handling

- Invalid route `chatId`: validation error.
- Chat not found for the authenticated user: `Chat.NotFound`.
- Empty patch body or `{}`: validation error.
- Explicit `null`: validation error.
- Unknown fields: validation error.
- Invalid `title`: reuse `ChatTitle` errors.

All errors flow through existing `ErrorOr` and `CustomResults.Problem` handling.

---

## 10. Testing

Per project instruction, test work should be added only if explicitly requested. If tests are requested, focus on:

- Domain transition tests for rename, hide/show, and archive/visibility independence.
- Handler tests for sparse application and full-state result.
- Endpoint/request parsing tests for omitted fields, explicit nulls, unknown fields, and empty patches.
- Repository cleanup tests proving hidden cleanup does not delete archived-visible chats.

---

## 11. Alternatives Considered

### Recommended: sparse PATCH with full response

One endpoint mirrors the observed ChatGPT behavior and returns the full state, matching the broader API preference for returning updated state after mutations.

### Sparse PATCH with minimal response

This would be smaller over the wire but less convenient for clients and less consistent with the desired API style.

### Separate action endpoints

Dedicated endpoints like `/pin`, `/archive`, and `/rename` are explicit but fragment a single resource update concern and do not reflect the observed ChatGPT contract.

---

## 12. Implementation Notes

- Use a presence-aware request representation. A plain DTO with `string? Title` is not enough if explicit `null` must be rejected.
- Reuse a single `now` value per handler execution for `PinnedAt` and `HiddenAt`.
- Do not update `UpdatedAt` for metadata-only changes.
- Keep `PinnedAt` stable across repeated `isPinned: true` requests.
- Keep `HiddenAt` stable across repeated `isVisible: false` requests.
- Do not let archive/unarchive mutate visibility.
- Do not let hide/show mutate archive state.
