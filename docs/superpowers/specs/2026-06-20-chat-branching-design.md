# Chat Branching Design

## Goal

Allow a user to branch an existing Nova chat into a separate chat from a selected assistant message. The browser mirrors ChatGPT's lazy branch flow: clicking **Branch in new chat** prepares a client-side draft, while the permanent branched chat is created only when the user sends the first new message.

The new chat is an independent snapshot. It copies the selected root-to-message path, receives new chat and message identifiers, and does not depend on the source chat after creation.

## Current Architecture Fit

Nova already represents each `ChatThread` as a branching message tree:

- `ChatThread` is the aggregate root.
- `ChatMessage.ParentMessageId` forms the tree.
- `ChatThread.CurrentMessageId` identifies the active branch head.
- `CreateChatHandler` persists the first user message and a generating assistant message, then publishes `TurnRequested` through the MassTransit EF outbox.
- `POST /v1/chats` is a FastEndpoints endpoint backed by a `Mediator` command.
- The repository loads chats owner-scoped and includes their messages.

Existing edit and regenerate operations create branches inside one `ChatThread`. This feature is different: it creates a new `ChatThread` containing a snapshot of one path from the source.

## Selected Approach

Use a domain-owned snapshot factory:

```text
ChatThread.BranchFrom(source, branchPointId, createdAt)
```

The aggregate validates the branch point, reconstructs its ancestor path, clones the path with new message IDs, and returns a new independent `ChatThread`. The application handler remains responsible for authentication, model usability, appending the submitted turn, persistence, and outbox publication.

This is preferred over application-layer copying because tree invariants stay inside the aggregate. It is preferred over a recursive SQL copy because it preserves domain rules and keeps the behavior database-independent. Nova chats are bounded enough that loading one source tree is appropriate.

## Frontend Lifecycle

The frontend flow is:

```text
/branch/{sourceChatId}/{sourceMessageId}
    -> /c/WEB:{clientGeneratedUuid}
    -> POST /v1/chats on the first send
    -> /c/{permanentChatId}
```

Clicking **Branch in new chat** does not mutate backend state. The frontend:

1. Retains `sourceChatId` and `sourceMessageId` from the `/branch/...` route.
2. Creates a client-only `WEB:<uuid>` draft identifier.
3. Navigates to `/c/WEB:<uuid>` and displays the source path through the selected message.
4. Includes the source IDs when the user submits the first new message.
5. Replaces the temporary URL with `/c/{chatId}` using the permanent ID returned by Nova.
6. Continues through the existing turn-streaming flow using the returned assistant message ID.

The `WEB:` identifier is not persisted or interpreted by the backend. A focused frontend integration document will record this lifecycle and its request/response contract; no frontend code is in scope.

## API Contract

### Endpoint

Continue using:

```http
POST /v1/chats
```

Extend the existing request with two optional fields:

```json
{
  "message": "Explore a different direction",
  "modelId": "a5e69b6d-b4f8-4d60-8f85-b59eb5541572",
  "forceUseSearch": false,
  "branchingFromChatId": "6a367f70-5f2c-83ed-bcde-39cd7687000a",
  "branchingFromMessageId": "5b117028-ef93-41fe-b961-434f82ba5425"
}
```

Both branching fields must be absent for normal chat creation or present for branch creation. Supplying only one is invalid.

Nova does not add ChatGPT's generic `action: "next"` field because the endpoint already defines the operation. It also does not require a duplicate `parentMessageId`; `branchingFromMessageId` is the selected parent in the copied snapshot.

### Success

Success remains:

```http
201 Created
Location: /v1/chats/{chatId}
```

The response remains the existing `TurnStartedResponse`:

```json
{
  "chatId": "...",
  "userMessageId": "...",
  "assistantMessageId": "..."
}
```

All returned identifiers belong to the new permanent chat.

### Request validation

- `message` and `modelId` retain their existing validation.
- Branch IDs must either both be null or both be present.
- A present branch ID cannot be `Guid.Empty`.
- `temporary-chat` is ignored as a branch-state override; a branch preserves the source chat's `IsTemporary` value.

## Branch Origin Value Object

Represent provenance as one nullable value object instead of two independently mutable aggregate properties:

```csharp
public sealed record ChatBranchOrigin
{
    public ChatId SourceChatId { get; }
    public ChatMessageId SourceMessageId { get; }
}
```

`ChatThread` exposes:

```csharp
public ChatBranchOrigin? BranchOrigin { get; private set; }
```

Normal chats have `BranchOrigin == null`. Only `ChatThread.BranchFrom` creates a thread with an origin. This makes an incomplete lineage pair unrepresentable in the domain.

The origin records only the immediate source. Branching an already branched chat therefore creates a lineage chain across independent snapshots without embedding the whole chain in each aggregate.

## Domain Snapshot Algorithm

`ChatThread.BranchFrom` returns `ErrorOr<ChatThread>` and performs these steps:

1. Find the selected message in the source thread.
2. Require the message to have role `Assistant`.
3. Require its status to be terminal: `Completed` or `Failed`.
4. Walk `ParentMessageId` from the selected message to a root while recording visited IDs.
5. Reject a cycle, missing ancestor, or a root that is not a user message as an invalid persisted path.
6. Reverse the collected messages into root-to-branch-point order.
7. Create a new `ChatId` and a mapping from each source message ID to a new `ChatMessageId`.
8. Copy each path message into the new chat, remapping its parent to the copied parent and assigning `SiblingIndex.First()`.
9. Preserve message role, content, model ID, status, failure reason, `CreatedAt`, and `CompletedAt`.
10. Set `CurrentMessageId` to the copied branch point.
11. Set the new thread's `BranchOrigin` to the immediate source chat and selected source message.

An internal `ChatMessage` branch-copy factory owns the message reconstruction. Public setters or application-layer reconstruction are not introduced.

Only the selected ancestor path is copied. Siblings, alternate branches, and descendants of the selected message are excluded.

### New thread metadata

- `UserId`: copied from the source.
- `Title`: `Branch: {source title}`.
- `CreatedAt` and initial `UpdatedAt`: branch creation time.
- `PinnedAt`: null.
- `IsArchived`: false.
- `IsTemporary`: copied from the source.
- `BranchOrigin`: immediate source chat/message pair.

The source-title portion is truncated when necessary so the complete prefixed title does not exceed `ChatTitle.MaxLength`. Branching an already branched chat adds another prefix, for example `Branch: Branch: Original`.

## Application Flow

Extend `CreateChatCommand` with nullable `BranchingFromChatId` and `BranchingFromMessageId` values. `CreateChatHandler` chooses one of two flows after its shared user, message, model, and model-usability validation.

### Normal creation

The current behavior remains unchanged: derive a title from the first message and call `ChatThread.Create`.

### Branch creation

1. Convert the branch IDs into `ChatId` and `ChatMessageId` value objects.
2. Load the source thread for the authenticated user through a no-tracking, owner-scoped repository query.
3. Return `Chat.NotFound` if the source does not exist for that owner.
4. Call `ChatThread.BranchFrom`.
5. Add the submitted user message beneath the copied branch point.
6. Begin a generating assistant message beneath the new user message using the requested model.
7. Add only the new thread to the repository.
8. Publish one `TurnRequested` carrying the new chat and assistant message IDs.
9. Save the new aggregate and outbox entry in the existing unit-of-work transaction.
10. Return `TurnStartedResult` using only new identifiers.

The source aggregate is never mutated or attached for updates.

## Persistence

Map `ChatBranchOrigin` as an optional EF Core complex property on `ChatThread` using two nullable columns on `chats`:

```text
branched_from_chat_id uuid null
branched_from_message_id uuid null
```

Do not add foreign keys. These values are historical provenance, not live dependencies. Deleting the source chat must not alter or delete a branch, and the origin identifiers remain useful as historical metadata even when their source rows no longer exist.

Add a check constraint requiring both columns to be null or both to be non-null. The domain value object is the primary invariant; the check protects against invalid direct database writes.

No lineage index is added because this scope does not query branches by source. Add one only when a descendant-listing feature requires it.

No changes are required to `chat_messages`: copied messages are ordinary rows owned by the new chat, with globally new IDs and remapped parent IDs.

The existing chat list and detail read contracts do not expose `BranchOrigin` in this scope. Persisting it now supports traceability and future branch-navigation features without coupling the current client contract to them.

## Errors

Validation errors return `400 Bad Request`:

- Only one branching ID was supplied.
- A supplied branching ID is empty or otherwise invalid.
- Existing message/model validation failed.

Not-found errors return `404 Not Found`:

- The source chat does not exist or belongs to another user: `Chat.NotFound`.
- The selected message is not present in that owner-scoped source: `Chat.MessageNotFound`.

Conflict errors return `409 Conflict`:

- The selected message is not an assistant message: `Chat.BranchPointMustBeAssistant`.
- The selected assistant message is still generating: `Chat.CannotBranchWhileGenerating`.

An ancestry cycle, missing ancestor, or invalid root indicates corrupted persisted state and returns an unexpected/server error such as `Chat.InvalidBranchPath`. No partial chat is persisted.

## Transaction and Concurrency Behavior

Model validation, snapshot creation, appending the new turn, and outbox publication complete before the unit of work commits. The new `ChatThread`, all copied and new messages, and the MassTransit outbox entry are committed atomically.

If any step fails:

- no branched chat is saved;
- no copied message is saved;
- no generation job is published.

The source is loaded without tracking and treated as an immutable snapshot. Later source edits, branch selection, archival, or deletion do not affect the new chat. If the selected assistant is generating when loaded, the operation returns a conflict; the client may retry after that turn becomes terminal.

This feature does not add a new idempotency mechanism. First-send retries have the same duplicate-create semantics as the existing normal `POST /v1/chats` flow.

## Testing Scope

The user approved focused domain and application test work.

### Domain tests

- `ChatBranchOrigin` stores the source IDs as one value.
- Branching copies exactly the selected ancestor path and excludes siblings and descendants.
- Every copied message receives a new ID and correctly remapped parent ID.
- Copied message content, model, status, failure information, and timestamps are preserved.
- Copied sibling indexes are zero.
- The new title, origin, temporary state, timestamps, pin/archive defaults, and current head are correct.
- Branching an already branched title adds another prefix without exceeding the title limit.
- Missing, user-role, and generating branch points are rejected.
- Cyclic, broken, or invalid-root paths are rejected as corrupted state.

### Application tests

- Normal creation remains unchanged when both branch fields are absent.
- Command validation requires both branch IDs together and rejects empty IDs.
- Source lookup is owner-scoped and missing/foreign sources save and publish nothing.
- Successful branching adds one new aggregate, appends one user and one generating assistant message, publishes one `TurnRequested`, and saves once.
- Returned chat and message IDs belong to the new thread.
- Model or domain failures save and publish nothing.

No endpoint tests or database integration tests are added in this scope.

## Frontend Documentation Deliverable

Add a focused repository document describing:

- `/branch/{sourceChatId}/{sourceMessageId}` as a frontend preparation route;
- `/c/WEB:{uuid}` as an unsaved client draft URL;
- the extended `POST /v1/chats` payload;
- replacement of the temporary URL with the returned permanent chat ID;
- normal streaming behavior after creation;
- expected handling for `400`, `404`, and `409` responses.

The document is a contract for the future frontend implementation. Building or modifying frontend code is outside this task.

## Out of Scope

- Frontend implementation.
- A backend endpoint invoked when the branch button is clicked.
- Persisting or accepting the `WEB:` draft identifier.
- Dynamically referencing source messages instead of copying them.
- Copying the source's full message tree.
- Listing descendant branches or exposing lineage in read responses.
- New idempotency behavior for chat creation.
- Changes to the existing turn worker or streaming transport.
- MassTransit upgrades.
