# Shared Chat Remix Design

## Goal

Let an authenticated viewer of a shared chat **remix** it: deep-copy the shared
root-to-selected-node conversation path into a new, independent `ChatThread` owned by the viewer.
The remix is a passive copy — the viewer lands in a normal chat of their own, ready to continue,
with no automatic generation. Remixing is available only when the sharer opted in.

This is the "importing or continuing a shared conversation" capability that the chat-sharing design
([`2026-06-21-chat-sharing-design.md`](2026-06-21-chat-sharing-design.md)) deliberately deferred as
out of scope. It reuses the deep-copy machinery already built for branching
([`2026-06-20-chat-branching-design.md`](2026-06-20-chat-branching-design.md)).

## Product Decisions

- Remixing is **opt-in by the sharer**, chosen once at share-creation time and **permanent**. The
  sharer cannot toggle it off later; the frontend warns that disabling it requires deleting the
  link and re-sharing. This keeps the immutable-share invariant intact and avoids the edge cases of
  flipping consent on a live link.
- The opt-in flag is the **consent** that authorizes the server to copy the sharer's content on
  behalf of another user. This is what justifies reading the source chat outside its owner scope.
- Remix produces a **fresh, independent chat every time** — no deduplication. Each call is its own
  copy.
- The remix is **passive**: it copies the path and stops. No `TurnRequested` is published and no
  assistant generation starts. The remixer continues the chat with the normal message flow.
- The copied node must be a **terminal assistant message**, exactly like branching. Remix
  eligibility is intentionally at least as strict as branching so the two features cannot drift.
- The new chat records a **`RemixOrigin`** (share ID, source chat ID, source message ID) for
  internal lineage. It is never surfaced in any read contract.
- The public read contract exposes **exactly one** new signal, `allowRemix`, so the frontend can
  render the Remix affordance. No source chat ID, owner identity, or other internal data is exposed.
- The new chat counts against the remixer's ordinary chat limits. No separate remix quota.
- Copying is text-only. Message content is stored inline in PostgreSQL; there are no attachments in
  scope.
- Focused domain, application, infrastructure, and endpoint tests are in scope.

## Current Architecture Fit

Nova already has the required foundations:

- FastEndpoints owns HTTP contracts and routes; `Mediator` dispatches commands and queries; results
  use `ErrorOr`.
- `SharedChat` (`Chat.Domain/SharedChats/SharedChat.cs`) already stores the source `ChatId`, the
  selected `CurrentMessageId`, a frozen `Title`, and the owner. A remix therefore already has a
  server-side pointer to everything it needs to copy — it only needs the consent flag.
- `ChatThread.BranchFrom` and `ChatMessage.CopyForBranch`
  (`Chat.Domain/Chats/ChatThread.cs`, `Chat.Domain/Chats/Entities/ChatMessage.cs`) already
  implement path validation, ID remapping, and per-message deep copy for a new aggregate.
- `ChatBranchOrigin` already demonstrates recording immediate lineage as nullable columns on `chats`
  with a both-or-neither check constraint and no foreign key.
- **Viewing a shared chat already requires authentication.** The public read handler resolves
  `IUserContext.UserId`; the link is an unguessable bearer secret, but only a signed-in user with it
  can read. The remixer therefore already has an authenticated identity at view time, so remix is a
  natural authenticated next step from the page — no new sign-in flow is required.

Remix belongs in the Chat service alongside sharing and branching. It requires no new service or
message-bus workflow.

## Selected Approach: Consent-Gated Server-Side Copy From the Stored Pointer

`shared_chats` already holds `(chat_id, current_message_id)`. When the share was created with remix
allowed, the server loads the source path via that stored pointer and deep-copies it into a new
`ChatThread` owned by the remixer. Nothing about the source is exposed to the client; the copy is
entirely server-side.

```text
shared_chats row (allow_remix = true)
  -> source chat_id + current_message_id
     -> load source path (root -> ... -> selected assistant node)
        -> deep-copy into new ChatThread owned by remixer
```

The sharer's opt-in is the authorization. Because the sharer consented, the copy step is allowed to
read the source aggregate without the usual owner scoping — the one owner-scope exception in the
system, and it is explicitly granted, not implicit.

### Alternatives considered

1. **Copy from the anonymous public projection** (the recursive-CTE read model rather than the
   source aggregate). This avoids ever loading the source aggregate outside owner scope, but it
   rebuilds a `ChatThread` from a flattened read DTO instead of reusing the domain deep-copy
   primitives, duplicating reconstruction logic. Rejected in favor of reusing `CopyForBranch` now
   that an explicit consent flag justifies the source read.
2. **Reuse the branch endpoint directly.** `BranchFrom` is owner-scoped (it loads the source via
   `GetSnapshotByIdAsync(chatId, userId)`) and requires the caller to own the source. A remixer is a
   different user and never learns the source chat ID, so branching cannot be reused verbatim. Remix
   reuses the lower-level copy primitive instead.

## Domain Model

Introduce a `ChatRemixOrigin` value object, mapped like `ChatBranchOrigin`:

```text
ChatRemixOrigin
  SharedChatId     SharedChatId
  SourceChatId     ChatId
  SourceMessageId  ChatMessageId
  Create(sharedChatId, sourceChatId, sourceMessageId)  -> ChatRemixOrigin   // domain-internal
```

`ChatThread` gains a nullable `RemixOrigin` alongside the existing nullable `BranchOrigin`.

### `ChatThread.CreateRemix`

```text
CreateRemix(remixerUserId, source, sharedNodeId, sharedChatId, title, createdAt)
  -> ErrorOr<ChatThread>
```

The factory (the source is guaranteed non-temporary because it was shareable, so no temporary
check is needed):

1. Finds `sharedNodeId` in `source`; returns `MessageNotFound` if absent.
2. Requires the node to be a **terminal assistant message**: `Role == Assistant` and
   `Status != Generating`. Otherwise returns `Chat.RemixTargetMustBeAssistant` (a conflict). This
   mirrors `BranchFrom` and is intentionally at least as strict as `ValidateShareAt`.
3. Walks `ParentMessageId` toward the root, tracking visited IDs; a cycle or missing ancestor
   returns `Chat.InvalidRemixPath`.
4. Confirms the root is a completed user message with no parent; otherwise `Chat.InvalidRemixPath`.
5. Builds a new `ChatId` and a source-to-copy message ID map, then reconstructs the linear path with
   `ChatMessage.CopyForBranch` (new IDs, remapped parents, `SiblingIndex.First()`), preserving each
   message's role, content, model ID, status, failure reason, and timestamps.
6. Initializes the new thread: owner `remixerUserId`, `Title` = the shared chat's frozen `title`
   (what the remixer saw), branch-time `CreatedAt`/`UpdatedAt`, no pin, non-archived, non-temporary,
   no project, head = the copied selected node, and `RemixOrigin = Create(sharedChatId, source.Id,
   sharedNodeId)`.

`source` is only read and is never mutated or persisted by this operation.

The terminal-assistant guard is deliberate. A passive copy does not technically need it, but
requiring it keeps remix and branch eligibility aligned and removes any "what if the node is a user
message" ambiguity. In practice every real share already satisfies it, because every user message
in this app immediately spawns a generating-then-completed assistant child, so a shared node is
always an assistant answer.

## Persistence

### `shared_chats`

Add one column:

```text
allow_remix  boolean  not null  default false
```

- Set only at creation. There is no update path, so it never changes for an existing row.
- The existing unique `(chat_id, current_message_id)` and idempotent `INSERT ... ON CONFLICT DO
  NOTHING` mean re-creating an existing pair returns the existing row and **cannot upgrade**
  `allow_remix`. To enable remix on a pair shared without it, the owner deletes the link and
  re-shares. This is consistent with the frozen title and timestamp.

### `chats`

Add three nullable columns for remix provenance:

```text
remixed_from_share_id     uuid  null
remixed_from_chat_id      uuid  null
remixed_from_message_id   uuid  null
```

Database check constraint (both-or-neither, mirroring `ChatBranchOrigin`):

```text
(remixed_from_share_id is null) = (remixed_from_chat_id is null)
and (remixed_from_share_id is null) = (remixed_from_message_id is null)
```

There is **no foreign key** from these columns to `shared_chats`, `chats`, or `chat_messages`. They
record historical origin, not a live dependency. Deleting the share, the source chat, or the source
message never cascades into or rewrites an already-remixed chat. Remix provenance is never exposed
in any read contract.

## API Contracts

### Create or reuse a share (amended)

```http
POST /v1/me/shared-chats
```

Request gains an optional field:

```json
{
  "conversationId": "6a338196-2a28-83ed-8999-e5273757f471",
  "currentNodeId": "24e7285c-f4b4-47d7-941c-0d7a1cdb81b5",
  "allowRemix": true
}
```

`allowRemix` defaults to `false`. The response echoes it:

```json
{
  "shareId": "03f5233b-37f9-4bf0-b18a-f5f43622573c",
  "shareUrl": "https://nova.example/share/03f5233b-37f9-4bf0-b18a-f5f43622573c",
  "title": "Conversation title",
  "allowRemix": true,
  "createdAt": "2026-07-10T14:49:12.553838Z",
  "alreadyExists": false
}
```

Re-creating an existing pair returns the existing row with its original `allowRemix` value
unchanged.

### Read a public link (amended)

```http
GET /v1/shared-chats/{shareId}
```

The response gains a single boolean so the frontend can render the Remix affordance:

```json
{
  "id": "03f5233b-37f9-4bf0-b18a-f5f43622573c",
  "title": "Conversation title",
  "allowRemix": true,
  "createdAt": "2026-07-10T14:49:12.553838Z",
  "currentNode": "24e7285c-f4b4-47d7-941c-0d7a1cdb81b5",
  "mapping": { }
}
```

No source chat ID, owner identity, or other internal data is added. `allowRemix` reveals only that
the link permits remixing.

### Remix a shared chat (new)

```http
POST /v1/shared-chats/{shareId}/remix
```

- **Authenticated.** The remixer's `IUserContext.UserId` owns the resulting chat. The request has no
  body in this scope (passive copy).
- A successful remix returns `201 Created` with `Location: /v1/chats/{newChatId}` and the new chat's
  identity:

```json
{
  "chatId": "b8b3f0d1-2c4a-4e6f-9a1b-7d2e5f0a1c3d",
  "title": "Conversation title",
  "createdAt": "2026-07-10T15:02:41.000000Z"
}
```

- Because remix is authenticated and mutates the remixer's account, it flows through the **existing
  authenticated BFF route with antiforgery**, not the specific anonymous-friendly `GET
  /v1/shared-chats/{shareId}` route. The remix path (`POST .../remix`) differs from the public read
  route in both method and path, so it falls through to the authenticated proxy naturally.
- Apply bounded per-client rate limiting, since each successful call creates a new chat.

## Application Flow

### `RemixSharedChatCommand`

Carries only `ShareId`. Its handler:

1. Resolves the remixer via `IUserContext`.
2. Loads the `shared_chats` header by `ShareId` (a new `GetHeaderByIdAsync` returning `allow_remix`,
   `chat_id`, `current_message_id`, and `title`). Returns `SharedChat.NotFound` (`404`) when absent
   or revoked.
3. Returns `Chat.RemixNotAllowed` (`403`) when `allow_remix` is `false`. The viewer can already read
   the share and its `allowRemix` flag, so a `403` reveals nothing new.
4. Loads the source `ChatThread` snapshot **by `chat_id` without owner scoping** (a new
   `GetSnapshotByChatIdAsync(chatId)`), justified by the sharer's consent. Returns `404` if the
   source is missing (share rows cascade-delete with their chat, so this is a defensive path).
5. Calls `ChatThread.CreateRemix(...)` with the frozen share title and read-once
   `IDateTimeProvider.UtcNow`.
6. Adds the new thread via the chat repository and calls `SaveChangesAsync`. It publishes **no**
   `TurnRequested` (passive copy).
7. Returns the new chat's identity.

The source is loaded no-tracking and is never added back, so remix cannot dirty or persist the
source aggregate.

## Lifecycle Rules

- A remixed chat is fully independent from creation. Deleting the share, the source conversation, or
  the source message never affects an existing remix (no FK, no cascade).
- `allow_remix` cannot be changed after creation. Re-sharing the same pair cannot upgrade it.
- Remixes created before a link is deleted survive the deletion; only future remixes stop.
- Account deletion for the remixer purges their remixed chats through the same chat lifecycle as any
  other chat they own.

## Security and Privacy

- The one owner-scope exception in the system — reading the source chat outside its owner — happens
  only when `allow_remix` is `true`, i.e. only under explicit sharer consent.
- The public read exposes `allowRemix` and nothing else new; no source identity leaks.
- `RemixOrigin` (which does contain the source chat and message IDs) is stored only server-side and
  is never returned by any read contract.
- Remix is authenticated and passes through the authenticated BFF route and its antiforgery
  protection.
- Rate-limit the remix endpoint to bound abuse, since it creates chats.

## Error Handling

Errors continue through `ErrorOr` and `CustomResults.Problem`.

### `400 Bad Request`
- Empty or malformed `shareId`.

### `403 Forbidden`
- `Chat.RemixNotAllowed` — the share was created without `allow_remix`.

### `404 Not Found`
- Share does not exist or is revoked.
- Source conversation is missing (defensive; normally impossible while the share row exists).

### `409 Conflict`
- `Chat.RemixTargetMustBeAssistant` — the selected node is not a terminal assistant message.

### `500 Internal Server Error`
- `Chat.InvalidRemixPath` — corrupt ancestry: a cycle, missing ancestor, or invalid root. Remix
  fails closed rather than copying a partial conversation.

## Testing Scope

The user approved focused test work.

### Domain tests
- `CreateRemix` copies exactly the root-to-node path with new chat and message IDs, remapped
  parents, and `SiblingIndex.First()`, preserving role, content, model ID, status, failure reason,
  and timestamps.
- The head is the copied selected node; the new thread is non-temporary, non-archived, unpinned,
  and project-less.
- `RemixOrigin` records the share ID, source chat ID, and source message ID.
- The new title equals the provided (frozen) share title.
- Rejects a generating terminal node, a user terminal node (`RemixTargetMustBeAssistant`), a missing
  node, a broken/cyclic ancestor path, and a non-completed-user root.
- The source aggregate is not mutated by a successful remix.

### Application tests
- Remix loads the share header by ID and returns `404` when missing.
- Remix returns `403` when `allow_remix` is `false`.
- Remix loads the source without owner scoping and makes the remixer the owner of the copy.
- A successful remix persists the new chat and publishes no `TurnRequested`.
- Two remixes of the same share create two distinct independent chats (no dedupe).

### Create-share tests
- `allowRemix` from the request is persisted and echoed; it defaults to `false`.
- Re-creating an existing pair does not change the stored `allow_remix`.

### Public read tests
- The public contract includes a correct `allowRemix` and still exposes no source identity.

### Infrastructure and endpoint tests
- The remix endpoint requires authentication and flows through the authenticated BFF route with
  antiforgery.
- A successful remix returns `201 Created` with a `Location` header to the new chat.
- Remix is rate-limited.

## Out of Scope

- Auto-continuing or generating on remix (the passive copy stops after copying).
- Editing an existing share's `allow_remix`, or any share mutation endpoint.
- Remix deduplication or a per-user remix quota beyond ordinary chat limits.
- Copying attachments or library files (none are wired into messages yet).
- Exposing remix provenance, lineage, or source identity in any read contract.
- Remixing at a non-assistant node.
- Frontend implementation of the Remix button, the opt-in toggle, and its permanence warning.
