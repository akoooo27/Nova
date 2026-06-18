# Get Chat With Messages Query — Design Spec

**Goal:** Give users a ChatGPT-style "open a conversation" call. A single `GET /chats/{chatId}` returns the chat's metadata plus its **entire message tree** — every node, in one response — shaped as a `mapping` keyed by message id with `parentId` + `children[]`, alongside `currentNode`. No message pagination.

**Builds on:** The existing `ChatThread` aggregate and its `ChatMessage` tree (`ParentMessageId`, `SiblingIndex`, `Status`, `Role`, `CurrentMessageId` = active head), the `chats` and `chat_messages` tables, the `GetChats` list query as the structural precedent (Query → Handler → reader interface → Dapper reader → ReadModels → Endpoint/Response/Mapper), the `UpdateChat` endpoint's `BaseEndpoint` + `SendErrorOrAsync` style, and the observed ChatGPT conversation-detail contract (full `mapping` + `current_node`, no pagination).

---

## 1. Scope

**In scope**

- `GET /chats/{chatId}` returning chat metadata + the full message tree + the current node id.
- ChatGPT-style `mapping`: an object keyed by message id; each node is `{ id, parentId, children[], message }`.
- `currentNode` = the chat's `CurrentMessageId`.
- Owner-scoped access; a chat that is missing or owned by another user returns `404 Chat.NotFound`.
- Assistant messages carry their model as `{ id, slug, name }` via a LEFT JOIN (`slug`/`name` null if the model was later deleted).

**Out of scope**

- Message pagination, cursors, or limits. The whole tree is returned in one call.
- Server-side computation of the active transcript path. The client walks `currentNode → parentId` to render it, exactly like ChatGPT.
- A synthetic root node (ChatGPT's `client-created-root`). Nova roots are real user messages with `parentId = null`.
- System / tool / reasoning message types. Nova currently stores only `User` and `Assistant` roles.
- Streaming. This endpoint returns a completed snapshot; live token streaming stays on its existing transport.

---

## 2. API Contract

### 2.1 Endpoint

```http
GET /chats/{chatId}
```

`{chatId}` binds to `Guid` (FastEndpoints route binding rejects non-Guids with `400`).

### 2.2 Success response

```http
200 OK
```

```json
{
  "id": "6a0c6313-8ff4-83eb-b6e6-dfee6af043e6",
  "title": "ACCA F3 არამატერიალური აქტივები",
  "isPinned": false,
  "pinnedAt": null,
  "isArchived": false,
  "isTemporary": false,
  "createdAt": "2026-05-19T13:18:26.202109+00:00",
  "updatedAt": "2026-05-20T14:14:27.361199+00:00",
  "currentNode": "8a3202ea-bd8e-4fb6-81f0-aab160a80326",
  "mapping": {
    "7f88f70c-9aad-4cf5-a5a2-2e5d58d0019a": {
      "id": "7f88f70c-9aad-4cf5-a5a2-2e5d58d0019a",
      "parentId": null,
      "children": ["f5117344-7c4c-4ff6-b40e-f043846c4daa"],
      "message": {
        "role": "user",
        "content": "ACCA(F3) არამატერიალური აქტივის განმარტება…",
        "status": "completed",
        "failureReason": null,
        "siblingIndex": 0,
        "model": null,
        "createdAt": "2026-05-19T13:18:25.582+00:00",
        "completedAt": "2026-05-19T13:18:25.582+00:00"
      }
    },
    "f5117344-7c4c-4ff6-b40e-f043846c4daa": {
      "id": "f5117344-7c4c-4ff6-b40e-f043846c4daa",
      "parentId": "7f88f70c-9aad-4cf5-a5a2-2e5d58d0019a",
      "children": [],
      "message": {
        "role": "assistant",
        "content": "ქვემოთ არის ACCA F3 / FA სტილის კონსპექტი…",
        "status": "completed",
        "failureReason": null,
        "siblingIndex": 0,
        "model": { "id": "a1b2…", "slug": "gpt-5-thinking", "name": "GPT-5 Thinking" },
        "createdAt": "2026-05-19T13:18:26.121+00:00",
        "completedAt": "2026-05-19T13:18:40.000+00:00"
      }
    }
  }
}
```

### 2.3 Mapping semantics

- One entry per `chat_messages` row for the chat. The key is the message id.
- `parentId` is the message's `ParentMessageId` (`null` for root user messages).
- `children` are the ids of messages whose parent is this node, ordered by `siblingIndex` (then `createdAt`, then `id`).
- `currentNode` is the chat's `CurrentMessageId`; the client reconstructs the active path by walking `parentId` from it to a root.
- `role` and `status` are serialized lowercase (`"user"`, `"assistant"`, `"generating"`, `"completed"`, `"failed"`) to match the reference contract.
- `model` is `null` for user messages and for assistant messages with no stored model. When an assistant message has a stored `llm_model_id`, `model.id` is always present; `model.slug` and `model.name` are `null` if that model has since been deleted.
- `content` is `null` while an assistant message is `generating` and for a `failed` message; `failureReason` is non-null only for `failed` messages.

### 2.4 Errors

- `400 Bad Request` — non-Guid `chatId` (route binding) or empty Guid (validator).
- `401 Unauthorized` — unauthenticated.
- `404 Chat.NotFound` — no chat with that id **for the authenticated user** (same response whether it does not exist or belongs to someone else, to avoid leaking ids).

---

## 3. Application Flow

All new types live under `Chat.Application/Chats/Queries/GetChat/`.

### 3.1 Query

```csharp
public sealed record GetChatQuery(Guid ChatId) : IQuery<ErrorOr<ChatDetailReadModel>>;
```

### 3.2 Validator

`GetChatQueryValidator : AbstractValidator<GetChatQuery>` — `ChatId` not `Guid.Empty` (mirrors `UpdateChatCommandValidator`). Auto-discovered and run by the existing `ValidationBehavior`.

### 3.3 Handler

`GetChatHandler` (`internal sealed`):

1. `UserId.Create(userContext.UserId)`; return its errors if invalid.
2. Build `ChatId` from `query.ChatId` (same construction `UpdateChatHandler` uses for the route id).
3. `await reader.GetAsync(chatId, userId, cancellationToken)`.
4. `null` ⇒ return `ChatOperationErrors.ChatNotFound(chatId)`.
5. Otherwise return the `ChatDetailReadModel`.

### 3.4 Reader interface

```csharp
public interface IChatDetailReader
{
    Task<ChatDetailReadModel?> GetAsync(ChatId chatId, UserId userId, CancellationToken cancellationToken);
}
```

### 3.5 Read models (flat)

The read models are a faithful flat projection. The tree/`mapping` shape is a presentation concern built in the API mapper (§5), so the reader stays simple and the wire shape can evolve without touching the query.

```csharp
public sealed record ChatDetailReadModel(
    Guid Id,
    string Title,
    bool IsPinned,
    DateTimeOffset? PinnedAt,
    bool IsArchived,
    bool IsTemporary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid CurrentMessageId,
    IReadOnlyList<ChatMessageReadModel> Messages);

public sealed record ChatMessageReadModel(
    Guid Id,
    Guid? ParentMessageId,
    MessageRole Role,
    string? Content,
    MessageStatus Status,
    string? FailureReason,
    int SiblingIndex,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    ChatMessageModelReadModel? Model);

public sealed record ChatMessageModelReadModel(
    Guid Id,
    string? Slug,
    string? Name);
```

`Model` is non-null exactly when the message has a stored `llm_model_id`; `Slug`/`Name` are null when the joined model row is absent (deleted).

---

## 4. Infrastructure (Reader)

`Chat.Infrastructure/Chats/Readers/ChatDetailReader.cs`, following the established Dapper reader pattern (`NpgsqlDataSource`, raw SQL, private `Row` records, registered in `Chat.Infrastructure/DependencyInjection.cs`).

Chat metadata and messages are fetched in one round-trip via `QueryMultipleAsync`. The first statement is owner-scoped — if it returns no row, the chat does not exist for this user and the reader returns `null` (the handler maps that to `Chat.NotFound`).

```sql
select id, title, pinned_at, is_archived, is_temporary,
       created_at, updated_at, current_message_id
from chats
where id = @ChatId and user_id = @UserId;

select m.id              as "Id",
       m.parent_message_id as "ParentMessageId",
       m.role            as "Role",
       m.content         as "Content",
       m.status          as "Status",
       m.failure_reason  as "FailureReason",
       m.sibling_index   as "SiblingIndex",
       m.created_at      as "CreatedAt",
       m.completed_at    as "CompletedAt",
       m.llm_model_id    as "ModelId",
       lm.external_model_id as "ModelSlug",
       lm.name           as "ModelName"
from chat_messages m
left join llm_models lm on lm.id = m.llm_model_id
where m.chat_id = @ChatId
order by m.created_at, m.id;
```

Notes:

- `role` and `status` are persisted as enum names (`HasConversion<string>`), so they map straight back into `MessageRole` / `MessageStatus`.
- The second statement is reached only after the first confirmed ownership; it is still naturally scoped by `chat_id`.
- `Model` is constructed only when `ModelId` is non-null: `new ChatMessageModelReadModel(ModelId.Value, ModelSlug, ModelName)`.
- `QueryMultipleAsync` keeps this to a single database round-trip. Read the chat grid first with `ReadSingleOrDefaultAsync`; if it is `null`, return `null` immediately (the messages grid is left unread — disposing the `GridReader` drains it). Otherwise read the messages grid and assemble the read model.

---

## 5. API Implementation

`Chat.Api/Endpoints/Chats/GetChat/` with `Endpoint.cs`, `Response.cs` (plus node/message/model response types), and `ResponseMapper.cs`.

The endpoint follows the `UpdateChat` style:

```csharp
internal sealed class Request
{
    public Guid ChatId { get; init; } // bound from route {chatId}
}

internal sealed class Endpoint(ISender sender) : BaseEndpoint<Request, Response>
{
    public const string RouteName = "Chat.Chats.Get";

    public override void Configure()
    {
        Get("/chats/{chatId}");
        Version(1);
        Options(b => b.WithName(RouteName));
        Description(/* 200 Response, 400, 401, 404; tag CustomTags.Chats */);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        ErrorOr<ChatDetailReadModel> result = await sender.Send(new GetChatQuery(request.ChatId), ct);
        await SendErrorOrAsync(result, ResponseMapper.ToResponse, cancellationToken: ct);
    }
}
```

`ResponseMapper.ToResponse(ChatDetailReadModel)`:

- Copies chat metadata and sets `currentNode = CurrentMessageId`.
- Builds `mapping` (a `Dictionary<string, MappingNodeResponse>` keyed by message id string):
  - Pre-group message ids by `ParentMessageId`, ordering each group by `SiblingIndex`, then `CreatedAt`, then `Id`, to produce each node's `children`.
  - For each message, emit `{ id, parentId, children, message }`.
  - `message.role` / `message.status` are lowercased enum names; `model` is mapped from `Model` (or `null`).

Use FastEndpoints and the existing `Mediator` package. No controllers, no MediatR.

---

## 6. Data Flow

```text
GET /chats/{chatId}
  -> FastEndpoints binds chatId (Guid) into Request
  -> GetChatQuery(chatId)
  -> ValidationBehavior rejects Guid.Empty (400)
  -> GetChatHandler resolves UserId, builds ChatId
  -> IChatDetailReader.GetAsync(chatId, userId)
       -> chat row owner-scoped (null -> handler returns Chat.NotFound -> 404)
       -> messages + model via LEFT JOIN (one QueryMultipleAsync)
  -> ChatDetailReadModel (flat messages)
  -> ResponseMapper builds ChatGPT-style mapping + currentNode
  -> 200 OK
```

---

## 7. Performance

A single conversation's tree is bounded (tens to low hundreds of messages), so loading all of it in one query is appropriate — and branch navigation requires the whole tree anyway. The unique index `chat_messages(chat_id, parent_message_id, sibling_index)` covers the `chat_id` filter; the LEFT JOIN to `llm_models` is a keyed lookup. No new index is required.

---

## 8. Testing

Per project instruction, tests are added only if explicitly requested (decided per the implementation plan). If requested, focus on:

- Handler: invalid user id surfaces errors; reader `null` ⇒ `Chat.NotFound`; populated read model passes through.
- Validator: rejects `Guid.Empty`; accepts a non-empty id.
- ResponseMapper: flat messages → correct `mapping` (parent/children wiring, sibling ordering, root has `parentId = null`); `currentNode` set; `model` null for user messages and present for assistant messages; lowercase `role`/`status`.

The Dapper reader stays manually verified, consistent with the repo (no Infrastructure test project).

---

## 9. Alternatives Considered

### Recommended: full tree in one call, ChatGPT-style mapping

Matches the observed ChatGPT conversation-detail contract and the conversation-tree spec's "full node tree" endpoint. One request opens a chat; the client owns branch navigation. Simplest correct model for an editable/branching tree.

### Flat `messages[]` array

Same information with less wire redundancy (no precomputed `children`), but the user chose the `mapping` shape for direct parity with a ChatGPT-style frontend renderer.

### Paginated messages

Rejected. A tree cannot be cleanly paged for branch navigation, conversations are bounded, and the reference performs no message pagination.

---

## 10. Implementation Notes

- Reuse the `GetChats` slice as the structural template; reuse the `UpdateChat` endpoint style (`BaseEndpoint` + `SendErrorOrAsync`).
- Keep the reader projection flat; build the `mapping` only in the API mapper.
- Return `404 Chat.NotFound` for both missing and other-user chats; never distinguish them.
- Construct `ChatId` from the route Guid the same way `UpdateChatHandler` does.
- Order `children` deterministically by `siblingIndex`, then `createdAt`, then `id`.
- Do not emit a synthetic root node; Nova roots have `parentId = null`.
- Register `ChatDetailReader` next to the other readers in `Chat.Infrastructure/DependencyInjection.cs`.
