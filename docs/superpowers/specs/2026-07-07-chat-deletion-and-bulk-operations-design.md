# Chat Deletion & Bulk Operations — Design

**Date:** 2026-07-07
**Status:** Approved (pending spec review)

## Goal

Give users the ability to:

1. Delete a single chat (`DELETE /chats/{chatId}`) — new; no per-chat delete exists today.
2. Archive all chats (`POST /me/chats/archive-all`).
3. Delete all chats (`DELETE /me/chats`).

And close a compliance gap: purge a user's chats when their account is deleted
(`UserDeletedConsumer` currently only marks the user read-model deleted — chat
content is retained forever).

## Decisions

### Hard delete, not soft delete

Deletion is a hard `DELETE` riding the existing cascade configuration. Rejected
alternatives:

- **Soft delete (`IsDeleted`/`DeletedAt`) with purge job** — legally valid, but
  every read path must filter deleted rows. The read side is split between EF
  Core and raw-SQL Dapper readers (chat list, FTS search, project chats); EF
  global query filters do not cover Dapper, so every current and future raw
  query becomes a leak risk. No restore/trash requirement exists to justify it.
- **Bare `IsDeleted` flag with no purge** — non-compliant (GDPR Art. 17 /
  storage limitation): data retained indefinitely after user deletion.

Legal baseline informing this: erasure must be real within a bounded window
(industry norm ≤ 30 days), but need not be instant; hard delete satisfies this
trivially. Archive remains the user's reversible option; the UI should put a
confirmation dialog in front of delete.

### "All" means all

- **Archive-all** targets every chat the user owns that is not temporary and
  not already archived — including chats inside projects.
- **Delete-all** targets every non-temporary chat the user owns — including
  archived chats and chats inside projects. Projects survive and become empty.
- **Temporary chats** are excluded from both user-facing operations (invisible
  in lists; `Chat.CleanupWorker` owns their lifecycle). They are **included**
  in the account-deletion purge.

### No batching

Both bulk operations are single set-based statements scoped to one user
(hundreds to low thousands of rows), served by the existing
`(UserId, UpdatedAt, Id)` index prefix. The 1000-row chunking in
`DeleteExpiredTemporaryChatsAsync` exists because it sweeps the whole table
across all users; that concern does not apply here.

## API Surface

| Route | Verb | Success | Errors |
|---|---|---|---|
| `/chats/{chatId}` | DELETE | 204 | 401; 404 if not found / not owned / temporary |
| `/me/chats/archive-all` | POST | 204 (idempotent) | 401 |
| `/me/chats` | DELETE | 204 (idempotent) | 401 |

All follow the `DeleteAllSharedChats` endpoint shape (FastEndpoints,
`EndpointWithoutRequest` where applicable, versioned, tagged, problem-details
errors).

## Application Layer

Three commands, each mirroring `DeleteAllSharedChatsHandler`: resolve `UserId`
via `IUserContext`, one repository call, `SaveChangesAsync`, return
`ErrorOr` result.

- `DeleteChatCommand(ChatId)` → `ErrorOr<Deleted>`; `ChatErrors.NotFound` when
  the delete affects zero rows.
- `ArchiveAllChatsCommand` → `ErrorOr<Success>`.
- `DeleteAllChatsCommand` → `ErrorOr<Deleted>`.

No domain events: `Archive()` raises none today, and deletion is terminal
(cascades handle all dependents). Bulk operations bypass the aggregate via
set-based SQL — the established pattern for bulk work in this codebase
(`DeleteExpiredTemporaryChatsAsync`, `ISharedChatRepository.DeleteAllAsync`).

## Repository (`IChatRepository`)

```csharp
Task<int> DeleteByIdAsync(ChatId id, UserId userId, CancellationToken ct = default);
// ExecuteDelete: Id == id && UserId == userId && !IsTemporary → affected count (0 ⇒ NotFound)

Task<int> ArchiveAllAsync(UserId userId, CancellationToken ct = default);
// ExecuteUpdate: UserId == userId && !IsTemporary && !IsArchived → IsArchived = true

Task<int> DeleteAllAsync(UserId userId, bool includeTemporary = false, CancellationToken ct = default);
// ExecuteDelete: UserId == userId (&& !IsTemporary unless includeTemporary)
```

`ArchiveAllAsync` sets **only** `IsArchived` — verified that
`ChatThread.Archive()` does not touch `UpdatedAt`, so bulk behavior matches
single-chat archive and archived-list ordering is preserved.

## Account-Deletion Purge

`UserDeletedConsumer` additionally calls `DeleteAllAsync(userId,
includeTemporary: true)` when marking the user deleted. The operation is
idempotent, so MassTransit redelivery is safe even though `ExecuteDelete`
runs outside the `SaveChangesAsync` transaction. Implementation must confirm
how the consumer's `UserReadModel` identity maps to the `UserId` stored on
`chat_threads`.

## Persistence Notes

- Cascades already configured and proven by temp-chat cleanup:
  `chat_messages` (including stored tsvector / GIN entries) and `shared_chats`
  rows go with the thread.
- The self-referencing `chat_messages.ParentMessageId` FK is `NO ACTION`;
  safe because Postgres checks it at statement end and the cascade removes
  parents and children in the same statement.
- No new indexes required.

## Edge Cases

- **Idempotency:** bulk endpoints return 204 even when zero rows match.
- **Delete during generation:** a turn in flight against a deleted chat fails
  at persistence (FK). Accepted — no coordination machinery; the user asked
  for the data to be gone.
- **Shared links:** cascade-deleted with the chat; links stop resolving.

## Future Notes

- When per-turn agent-session snapshots land (keyed by
  `assistant_message_id`), they must cascade from `chat_messages` or the
  delete paths will orphan them. No such tables exist today.
- A "recently deleted" trash/restore UX would be a separate spec (soft delete
  + purge worker + read-path filtering); nothing here precludes it.

## Testing

- Handler unit tests: invalid user id path; repository invocation; NotFound on
  zero-row single delete.
- Integration tests (follow existing handler test patterns):
  - archive-all: skips temporary and already-archived chats, includes project
    chats, does not touch other users' chats, does not change `UpdatedAt`.
  - delete-all: removes chats, messages, and shared-chat rows; leaves
    projects and other users' data intact; excludes temporary chats.
  - single delete: owner-scoped (other user's chat ⇒ 404, row untouched);
    temporary chat ⇒ 404.
  - account purge: `UserDeleted` consumption removes all chats including
    temporary ones; redelivery is a no-op.
