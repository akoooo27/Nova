# Chat Sharing Design

## Goal

Allow authenticated Nova users to create anonymous public links to selected points in their non-temporary chats, list all links they own, revoke one link, or revoke all links together. Anyone with a valid link can read the shared conversation without signing in.

Each link represents the visible root-to-selected-node path at the time it is created. A link is unique for one `(conversationId, currentNodeId)` pair. Repeating creation for the same active pair returns the same URL; sharing another node in the same conversation creates another link.

This scope also corrects regeneration so only the current active assistant node can be regenerated.

## Product Decisions

- Shared pages never expose the owner's name, user ID, or other attribution.
- Shared pages are accessible without authentication to anyone who possesses the URL.
- Temporary chats cannot be shared.
- A shared view contains only the selected node and its ancestors. Siblings, alternate branches, and descendants are excluded.
- Existing links do not change when the source chat gains messages, branches, or regenerations.
- Deleting a source conversation revokes all links for that conversation.
- The same conversation may have multiple links when each link selects a different node.
- Repeating creation for an existing conversation/node pair returns the existing link without updating its title or timestamp.
- Deleting a link and later sharing the same pair creates a new random URL. A revoked URL is never reactivated.
- Owner link listing uses bounded offset pagination, ordered newest first.
- Nova uses compact camelCase contracts rather than ChatGPT's null-heavy compatibility shape.
- Moderation and PII fields are omitted because Nova has no system that can truthfully populate them.
- Focused domain, application, infrastructure, and endpoint tests are in scope.

## Current Architecture Fit

Nova already has the required foundations:

- FastEndpoints owns HTTP contracts and routes.
- `Mediator.SourceGenerator` / `Mediator.Abstractions` dispatch commands and queries.
- `ChatThread` is an owner-scoped aggregate containing a branching message tree.
- `ChatMessage.ParentMessageId` identifies ancestry.
- `ChatThread.CurrentMessageId` identifies the active branch head.
- Completed messages are not changed in place. Regeneration and the domain's dormant edit capability create siblings.
- EF Core/PostgreSQL handle writes and aggregate persistence.
- Dapper readers serve chat list and detail projections.
- The BFF proxies authenticated Chat API requests with a user access token.

Sharing belongs in the Chat service. It does not require another service or message-bus workflow.

## Selected Approach: Reference-Backed Immutable View

Store one sharing record containing the source conversation and selected node rather than copying messages. The public reader starts at that exact node and follows parent links toward the root.

```text
shared node -> parent -> parent -> root
```

It never reads `ChatThread.CurrentMessageId` and never follows children. A later regeneration creates a sibling with a new ID, so it cannot enter the stored node's ancestor path.

This provides snapshot behavior under an explicit append-only invariant: terminal message content and parent links cannot be mutated. Editing or regenerating an old point creates a new branch while the old branch remains stored, even if the owner UI hides it after switching to the new active path.

```text
old hidden path: root -> old prompt -> old answer -> shared node
new active path: root -> edited prompt -> new answer
```

The old link continues to traverse the hidden path. Sharing the new path uses its new selected node and therefore creates a different link.

If Nova later introduces destructive in-place message editing or branch pruning, that operation must either revoke every affected link atomically or migrate affected links to copied snapshots. It must never leave a link that silently displays changed or incomplete content.

### Alternatives considered

1. **Normalized copied snapshot:** copy the selected path into `shared_chat_messages`. This gives the strongest physical isolation but duplicates every shared message and adds synchronization and schema surface that Nova's append-only tree does not currently need.
2. **JSON copied snapshot:** store the path in one `jsonb` value. Creation is compact, but relational integrity and schema evolution are weaker.
3. **Unbounded live conversation:** store only a conversation ID and render its active path. This is rejected because new messages and branch changes would alter existing links.

The selected design is not an unbounded live conversation. It is pinned to one immutable node and one immutable ancestor chain.

## Domain Model

Introduce a `SharedChat` aggregate/entity and value object ID:

```text
SharedChat
  Id                SharedChatId
  UserId            UserId
  ConversationId    ChatId
  CurrentNodeId     ChatMessageId
  Title             ChatTitle
  CreatedAt         DateTimeOffset
```

`SharedChatId.New()` uses a random UUIDv4 rather than Nova's usual time-ordered UUIDv7 because the ID is a public bearer secret.

The title is frozen when the link is first created. Renaming the source conversation later does not change an existing shared page. Repeating creation for an existing pair also does not overwrite the title.

Sharing eligibility remains domain-owned. A `ChatThread` operation validates that:

- the chat is not temporary;
- the selected node exists in that chat;
- the selected node is not a generating assistant message;
- walking parents reaches a root without a missing ancestor or cycle;
- the root is a completed user message with no parent.

The selected node may be historical and does not have to equal `ChatThread.CurrentMessageId`. This permits sharing a specific visible response or hidden historical branch. Latest-node equality is required only for regeneration.

## Persistence

Add a `shared_chats` table:

```text
id               uuid        primary key
user_id          text        not null
conversation_id  uuid        not null
current_node_id  uuid        not null
title            text        not null
created_at       timestamptz not null
```

Constraints and indexes:

- Unique `(conversation_id, current_node_id)` enforces one active link per pair.
- `conversation_id` references `chats(id)` with `ON DELETE CASCADE`.
- `(conversation_id, current_node_id)` references `chat_messages(chat_id, id)` with `ON DELETE CASCADE`; add the required unique key on `(chat_id, id)`. This guarantees that the selected node belongs to the source conversation.
- Index `(user_id, created_at DESC, id DESC)` supports owner listing.

No message content is stored in `shared_chats`. There is no `updated_at` because links are immutable in this scope.

### Idempotent creation

An application-level check alone is insufficient because two requests can race. `ISharedChatRepository.CreateOrGetAsync` performs a PostgreSQL transaction:

1. Attempt an `INSERT` with `ON CONFLICT (conversation_id, current_node_id) DO NOTHING` and a `RETURNING` clause for the complete shared-chat row.
2. If the insert returns a row, return it with `AlreadyExists = false`.
3. If it returns no row, select the committed existing pair and return it with `AlreadyExists = true`.

PostgreSQL waits for a conflicting in-flight insert before resolving `ON CONFLICT`. The follow-up statement receives a fresh `READ COMMITTED` snapshot, so concurrent callers converge on one row and one URL. Conflict handling never performs a no-op update and therefore never changes the stored title or timestamp.

## API Contracts

All routes use FastEndpoints versioning and `Mediator`, not ASP.NET Core controllers or MediatR.

### Create or reuse a link

```http
POST /v1/me/shared-chats
```

```json
{
  "conversationId": "6a338196-2a28-83ed-8999-e5273757f471",
  "currentNodeId": "24e7285c-f4b4-47d7-941c-0d7a1cdb81b5"
}
```

```json
{
  "shareId": "03f5233b-37f9-4bf0-b18a-f5f43622573c",
  "shareUrl": "https://nova.example/share/03f5233b-37f9-4bf0-b18a-f5f43622573c",
  "title": "Conversation title",
  "conversationId": "6a338196-2a28-83ed-8999-e5273757f471",
  "currentNodeId": "24e7285c-f4b4-47d7-941c-0d7a1cdb81b5",
  "createdAt": "2026-06-21T14:49:12.553838Z",
  "alreadyExists": false
}
```

- A newly inserted link returns `201 Created` and a `Location` header containing the frontend share URL.
- An existing pair returns `200 OK` with `alreadyExists: true`.
- `isAnonymous` is omitted because anonymity is a system invariant, not a caller choice.

### List owned links

```http
GET /v1/me/shared-chats?limit=50&offset=0
```

```json
{
  "items": [
    {
      "id": "03f5233b-37f9-4bf0-b18a-f5f43622573c",
      "shareUrl": "https://nova.example/share/03f5233b-37f9-4bf0-b18a-f5f43622573c",
      "title": "Conversation title",
      "conversationId": "6a338196-2a28-83ed-8999-e5273757f471",
      "currentNodeId": "24e7285c-f4b4-47d7-941c-0d7a1cdb81b5",
      "createdAt": "2026-06-21T14:49:12.553838Z"
    }
  ],
  "total": 1,
  "limit": 50,
  "offset": 0
}
```

- Default limit: `50`.
- Maximum limit: `100`.
- Default offset: `0`.
- Order: `created_at DESC, id DESC`.

### Delete one owned link

```http
DELETE /v1/me/shared-chats/{shareId}
```

The repository deletes only where both `id` and the authenticated `user_id` match. Success returns `204 No Content`. A missing or foreign-owned ID returns the same `404 Not Found` response.

### Delete all owned links

```http
DELETE /v1/me/shared-chats
```

One owner-scoped database statement deletes all matching rows. The operation is idempotent and returns `204 No Content`, including when the owner has no links.

### Read a public link

```http
GET /v1/public/shared-chats/{shareId}
```

This endpoint calls `AllowAnonymous()` and returns a dedicated public contract:

```json
{
  "id": "03f5233b-37f9-4bf0-b18a-f5f43622573c",
  "title": "Conversation title",
  "createdAt": "2026-06-21T14:49:12.553838Z",
  "currentNode": "24e7285c-f4b4-47d7-941c-0d7a1cdb81b5",
  "mapping": {
    "18180b17-c55b-4c95-8e03-ddae8f68240e": {
      "id": "18180b17-c55b-4c95-8e03-ddae8f68240e",
      "parentId": null,
      "children": [
        "24e7285c-f4b4-47d7-941c-0d7a1cdb81b5"
      ],
      "message": {
        "role": "user",
        "content": "Explain immutable shared links.",
        "status": "completed",
        "createdAt": "2026-06-21T14:48:50.000000Z",
        "completedAt": "2026-06-21T14:48:50.000000Z"
      }
    },
    "24e7285c-f4b4-47d7-941c-0d7a1cdb81b5": {
      "id": "24e7285c-f4b4-47d7-941c-0d7a1cdb81b5",
      "parentId": "18180b17-c55b-4c95-8e03-ddae8f68240e",
      "children": [],
      "message": {
        "role": "assistant",
        "content": "Pin the link to one selected node and retain its ancestor path.",
        "status": "completed",
        "createdAt": "2026-06-21T14:49:00.000000Z",
        "completedAt": "2026-06-21T14:49:12.000000Z"
      }
    }
  }
}
```

The mapping contains exactly one linear root-to-selected-node path. It omits owner identity, source conversation ID, pin/archive/temporary metadata, sibling indexes, internal failure details, moderation state, PII flags, and discoverability controls.

## Application Flow

### Create

`CreateSharedChatCommand` carries `ConversationId` and `CurrentNodeId`. Its handler:

1. Creates `UserId`, `ChatId`, and `ChatMessageId` value objects.
2. Loads the source through an owner-scoped chat repository query.
3. Returns `Chat.NotFound` when the source is absent or belongs to another user.
4. Runs domain sharing-eligibility validation.
5. Creates a candidate `SharedChat` using the source's current title and `IDateTimeProvider.UtcNow` read once.
6. Calls the atomic create-or-get repository operation.
7. Builds `shareUrl` from `SharedLinksOptions.PublicBaseUrl`.
8. Returns the shared-chat result and whether it already existed.

The public base URL is validated at startup as an absolute URI. Production requires HTTPS; local development may use an HTTP localhost address.

### List

`GetSharedChatsQuery` validates pagination, resolves the current user, and calls a Dapper reader. The reader executes an owner-scoped count and page query and returns one read model containing `Items`, `Total`, `Limit`, and `Offset`.

### Delete

`DeleteSharedChatCommand` and `DeleteAllSharedChatsCommand` resolve the current user and call owner-scoped repository methods. They do not load source conversations or message trees.

### Public read

`GetPublicSharedChatQuery` carries only `ShareId`; it does not require `IUserContext`. A Dapper reader:

1. Loads the share header by ID.
2. Anchors a recursive PostgreSQL CTE at `(conversation_id, current_node_id)`.
3. Follows `parent_message_id` only within the same conversation.
4. Tracks visited IDs so corrupt cycles cannot recurse forever.
5. Confirms the result terminates at one valid root.
6. Orders nodes root-to-leaf and builds the public mapping.

The query never uses the source chat's current head. A nonexistent, revoked, or cascade-deleted link returns `404` without revealing which case occurred.

## BFF and Frontend Routing

The existing BFF catch-all Chat API route requires a user access token. Add a more-specific YARP route for:

```text
GET /api/chat/v1/public/shared-chats/{shareId}
```

It forwards to the same Chat API cluster but does not attach an access token. Restrict the route to `GET`; all owner operations continue through the existing authenticated route and antiforgery checks.

`SharedLinksOptions.PublicBaseUrl` produces frontend URLs such as:

```text
https://nova.example/share/{shareId}
```

The frontend fallback serves that page, which reads the public endpoint. Frontend implementation is outside this backend design, but the route and response are its contract.

## Lifecycle Rules

- Deleting one share revokes only that URL.
- Deleting all shares revokes all URLs owned by that user but leaves source conversations unchanged.
- Deleting a source conversation cascades to every share for that conversation.
- Deleting a selected message cascades to links anchored at that message. Nova currently has no individual message-deletion operation.
- Hidden branches remain persisted while links depend on them.
- Account deletion must ultimately delete the user's source chats or shares. The current identity projection only marks users deleted, so full account-data erasure remains a separate lifecycle concern.
- Revocation is immediate at the origin; responses are not publicly cached.

## Security and Privacy

- A share URL is an uncredentialed bearer secret. Use UUIDv4 entropy and never log URLs or IDs as security credentials at elevated verbosity.
- Public responses contain no owner attribution.
- Add `Cache-Control: no-store` to public API responses so a revoked link is not retained by shared caches.
- Add `X-Robots-Tag: noindex, nofollow`; the frontend page should carry equivalent robot metadata.
- The frontend page should use `Referrer-Policy: no-referrer` to avoid leaking share URLs to outbound sites.
- Apply bounded per-client rate limiting to the anonymous endpoint.
- Return `404` for every unavailable public link rather than distinguishing nonexistent and revoked links.
- Keep owner mutations behind the authenticated BFF route and its antiforgery protection.
- Do not advertise discoverability, expiration, granular recipients, moderation, or PII scanning.

## Error Handling

Errors continue through `ErrorOr` and `CustomResults.Problem`.

### `400 Bad Request`

- Empty or malformed IDs.
- Negative offset.
- Limit outside `1..100`.

### `404 Not Found`

- Source conversation does not exist or is not owned by the caller.
- Selected node does not exist in that conversation.
- Individual share does not exist or is not owned by the caller.
- Public share is missing, revoked, or cascade-deleted.

### `409 Conflict`

- `Chat.CannotShareTemporaryChat`.
- `Chat.CannotShareGeneratingMessage`.
- `Chat.RegenerationTargetMustBeCurrent`.
- An optimistic concurrency conflict during regeneration.

### `500 Internal Server Error`

- A persisted parent path contains a cycle, missing ancestor, or invalid root.
- An unexpected persistence failure occurs.

Corrupt ancestry never returns a partial public conversation.

## Regeneration Correction

The current implementation accepts any terminal assistant message. That behavior came from the earlier regeneration design, which did not define latest-only regeneration.

Amend `ChatThread.RegenerateAssistant` so, after confirming the target exists and is an assistant, it also requires:

```text
messageId == CurrentMessageId
```

Otherwise return `Chat.RegenerationTargetMustBeCurrent` as a conflict. This prevents an API caller from regenerating a stale assistant node after later turns or another regeneration have moved the active head.

Keep the existing checks that the target has a parent and is not generating. A successful operation still creates a generating assistant sibling under the same user message and moves the head to the new sibling.

`ChatThread` already maps PostgreSQL `xmin` as an optimistic concurrency token. Two callers can read the same current node, but only one aggregate update can commit. Translate `DbUpdateConcurrencyException` at the infrastructure/API boundary into a generic optimistic-concurrency error and return `409 Conflict`; do not expose EF Core types from the application layer.

## Testing Scope

The user approved focused test work.

### Domain tests

- Sharing rejects temporary chats.
- Sharing rejects a missing node and a generating assistant node.
- Sharing accepts a terminal historical node that belongs to the chat.
- Sharing detects broken ancestry, cycles, and an invalid root.
- `SharedChat` freezes the source title, source IDs, owner, and creation time.
- `SharedChatId` generation produces nonempty random IDs.
- Regeneration succeeds for the current terminal assistant.
- Regeneration rejects a terminal assistant that is not `CurrentMessageId`.
- Regeneration still rejects user and generating targets.

### Application tests

- Creation is owner-scoped and rejects a foreign source as not found.
- A new pair returns `alreadyExists: false`.
- Repeating one pair returns the same ID and URL with `alreadyExists: true`.
- A different node in the same conversation creates another link.
- Repeating an existing pair does not refresh its title or timestamp.
- Deleting and recreating a pair produces a new ID.
- Individual deletion cannot delete another user's link.
- Bulk deletion affects only the current user.
- Pagination defaults, maximum, ordering, total, and offsets are correct.
- Regeneration handler saves and publishes nothing for a stale target.

### Infrastructure and endpoint tests

- Concurrent create requests converge on one database row.
- The recursive reader returns only the selected ancestor chain.
- Later descendants, regenerations, and sibling branches do not appear.
- Source-conversation deletion cascades to all of its links.
- Public retrieval works anonymously.
- Revoked, foreign, and unknown links return the documented status.
- Public responses include `Cache-Control` and robot headers.
- Owner endpoints still require authentication; mutation requests retain antiforgery enforcement through the BFF.
- A concurrent regeneration loser is returned as `409` rather than `500`.

## Out of Scope

- Frontend page implementation and shared-links management UI.
- Showing owner identity or allowing callers to disable anonymity.
- Link updates, mutable titles, or re-pointing an existing link to another node.
- Expiring links, password protection, recipient allowlists, or workspace-only links.
- Search-engine discoverability.
- Importing or continuing a shared conversation.
- Moderation, abuse reporting, and PII detection.
- Sharing temporary chats.
- Individual message editing or deletion endpoints.
- Copying shared messages unless Nova later introduces destructive mutation or pruning.
- Full user-account data erasure, which requires a separate identity lifecycle design.
