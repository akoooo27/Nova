# Turn Lifecycle Race Hardening — Design

**Date:** 2026-07-07
**Status:** Approved (pending spec review)
**Follows:** `2026-07-07-chat-deletion-and-bulk-operations-design.md`

## Problem

Chat deletion (single, delete-all, account purge) and archive-all are set-based
`ExecuteDelete`/`ExecuteUpdate` statements that can land while a turn is
generating. The turn pipeline loads the `ChatThread` once, streams tokens to
Redis only, and persists everything in a single `SaveChangesAsync` at the end —
a race window spanning the entire LLM generation. Observed failure modes:

1. **Archive (or any chat mutation) mid-turn** — `ExecuteUpdate` bumps the
   `xmin` concurrency token; the turn's terminal save throws an unhandled
   `DbUpdateConcurrencyException`; MassTransit redelivers; the message is still
   `Generating` (rollback), so the **entire turn re-runs**: the Redis stream the
   client is reading is reset mid-read and the LLM regenerates at full cost.
2. **Delete mid-turn** — nothing signals the in-flight turn; the LLM generates
   to completion into a deleted chat; the terminal save throws the same
   unhandled exception; retries reload, find nothing, and no-op.
3. **Redis retention leak** — `RedisStreamTokenPublisher` sets the stream TTL
   only on terminal events. When a turn dies before one (case 2, worker crash),
   the full generated content of a *deleted* chat sits in Redis with no expiry —
   directly undermining the erasure guarantee that motivated hard delete.
4. **Client hang** — in every delete case the stream reader never receives a
   terminal event.
5. **SendMessage vs delete** — a delete landing between `SendMessageHandler`'s
   load and save surfaces as an unhandled concurrency exception (HTTP 500)
   instead of 404.

## Decision: delete wins

Deletion always proceeds; the turn pipeline yields. Rejected alternative:
block/skip deletion while a message is `Generating`. That is a
time-of-check-to-time-of-use race (a turn can start right after the check, and
the orchestrator-side failures remain), it makes chats with stuck-`Generating`
messages (dead-lettered turns) permanently undeletable with no reconciliation
job, and the account purge cannot legally skip chats. A "generation is running —
are you sure?" prompt is client UX, not a server-side block.

## Design

### 1. Terminal-save conflict handling in `ChatTurnOrchestrator` (core fix)

The three terminal saves (complete `:174`, fail `:203`, stop `:249`) share a
persistence helper. On `DbUpdateConcurrencyException` it reloads the thread via
`IChatRepository.GetByIdAsync` and branches:

- **Thread gone (deleted mid-turn):** log at information level, publish a
  terminal `StoppedEvent` to the stream, return. Publishing the terminal event
  unblocks any hanging reader and applies the existing 10-minute stream TTL, so
  deleted content ages out. No rethrow — the redelivery/no-op cycle and
  dead-letter noise disappear.
- **Thread exists, `xmin` moved (archived/renamed/pinned/moved mid-turn):**
  reapply the terminal transition (`CompleteAssistantMessage` /
  `FailAssistantMessage` / `StopAssistantMessage`) to the freshly loaded
  instance and save again. The generated text is already in memory; the retry
  costs one reload instead of a full LLM regeneration. Loop this
  reload-and-reapply up to 3 attempts (conflicts require a concurrent
  chats-row mutation each time, so the loop converges); if attempts are
  exhausted, log an error and publish `FailedEvent` so the client is not left
  hanging.
- If the reloaded message is already terminal (concurrent stop/regenerate),
  keep the existing idempotent no-op behavior.

This is deliberately generic — it fixes archive-all and every other concurrent
chat mutation, not just this feature's endpoints.

### 2. Delete signals stop (cuts wasted LLM spend)

`DeleteChatHandler`, `DeleteAllChatsHandler`, and the `UserDeletedConsumer`
purge path gain a best-effort post-delete step:

1. Before the `ExecuteDelete`, query the in-scope `Generating` message IDs —
   new `IChatRepository.GetGeneratingMessageIdsAsync(UserId userId,
   ChatId? chatId = null)` (EF `SelectMany` over `ChatThreads.Messages`,
   served by the existing `status` index).
2. Run the delete exactly as today.
3. Fire `ITurnStopSignal.RequestStopAsync` for each ID, inside a try/catch —
   deletion must never fail or roll back because Redis is unavailable.

The orchestrator polls the stop flag every event, so the turn halts within a
token or two. Signal-after-delete ordering is safe: a stop flag for a turn that
already finished is a no-op and the flag self-expires in 10 minutes.

### 3. Stream TTL from birth (leak backstop)

`RedisStreamTokenPublisher.PublishAsync` sets a sliding 30-minute TTL on every
`StreamAddAsync` (pipelined with the add, not awaited separately for terminal
events which keep the tighter 10-minute TTL). Piece 1 covers the known paths;
this makes the no-indefinite-retention guarantee unconditional across worker
crashes and future bugs. 30 minutes comfortably exceeds any legitimate turn
duration.

### 4. `SendMessageHandler` conflict → 404

Catch `DbUpdateConcurrencyException` around its `SaveChangesAsync`; return
`ChatOperationErrors.ChatNotFound` when the thread no longer exists (reload to
confirm). The user deleted the chat; "not found" is the truthful answer, not a
500.

## Non-goals

- No `IsArchived` guard on `SendMessage` (sending to an archived chat is
  currently allowed product behavior; changing it is a separate decision).
- No distributed locks, no turn-state tables, no soft delete — the domain
  stays persistence-ignorant and the fix stays in the application/infrastructure
  layers.
- No client work (the client already handles `StoppedEvent`/`FailedEvent`).

## Testing

- Orchestrator unit tests (extend `FakeChatRepository` + fake unit of work that
  throws `DbUpdateConcurrencyException` on demand):
  - conflict + thread gone → `StoppedEvent` published, no throw, no retry loop;
  - conflict + thread present → transition reapplied to reloaded thread, saved,
    `DoneEvent` published, content intact;
  - conflict persists past max attempts → `FailedEvent` published, error logged;
  - reloaded message already terminal → no-op.
- Delete/bulk handler tests: generating IDs queried, stop signal fired per ID,
  Redis failure does not fail the command, delete still executes.
- Publisher test: TTL present on stream key after first non-terminal event.
- Integration test: delete mid-turn → turn ends with `StoppedEvent`, stream key
  has TTL, no dead-letters.
