# Stop Generation With Partial Assistant Content - Design Spec

**Date:** 2026-06-24
**Status:** Approved design, pre-implementation

**Goal:** Let a user stop an in-flight assistant turn and keep the text the assistant already generated. The stopped assistant message becomes a durable terminal message with partial content, not a failure and not a resumable pause.

**Builds on:** The existing chat turn pipeline described in `docs/superpowers/plans/2026-06-12-chat-turn-pipeline.md`: the assistant `ChatMessage` is the turn lifecycle, `ChatTurnOrchestrator` sequences the run, `TurnEvent` is append-only, Redis Streams carry live events, and all durable state transitions go through `ChatThread`.

---

## 1. Scope

**In scope**

- A user can request stop for a generating assistant message.
- The live stream ends with a stop event.
- The assistant message persists the partial content generated before the stop was observed.
- The stopped message is terminal and can be followed by a new user reply.
- Regenerate, branch, and share should treat a stopped assistant message like other terminal assistant messages, subject to existing feature-specific validation.

**Out of scope**

- Provider-specific hard cancellation as the primary mechanism.
- Resuming a stopped generation.
- New turn, job, execution, or cancellation lifecycle entities.
- Changes to prompt/context building.
- Tool, memory, or MassTransit version changes.

---

## 2. User Semantics

Stop generation means "finish this turn early with the partial answer already visible to the user."

The client continues rendering token events normally. When the stop is observed, the stream emits a terminal `StoppedEvent`. The client then marks the assistant message as stopped while keeping the text already displayed. On reload, the message content comes from durable storage, not from the Redis stream.

This is intentionally not called `Paused`. A stopped provider stream is not expected to resume from the same provider request later.

---

## 3. Architecture Rule Compliance

This feature must preserve the binding rules from `2026-06-12-chat-turn-pipeline.md`:

- **No new lifecycle entity:** the assistant `ChatMessage` remains the durable turn lifecycle.
- **Orchestrator stays sequencing-only:** introduce a stop-signal seam instead of adding storage or transport details inline.
- **All state transitions go through the aggregate:** add `ChatThread.StopAssistantMessage(...)`; never set status directly through SQL, EF property mutation outside the aggregate, or Redis state.
- **TurnEvent is append-only:** add `StoppedEvent`; do not change existing event shapes or discriminators.
- **Agent Framework remains quarantined:** no `Microsoft.Agents.AI` or `Microsoft.Extensions.AI` types outside the existing infrastructure adapter area.
- **ContextBuilder remains unchanged:** stop state does not affect prompt assembly.
- **Memory remains no-op:** no memory behavior belongs in this feature.
- **MassTransit remains pinned:** do not upgrade or redesign the queueing layer.

---

## 4. Domain Model

Extend `MessageStatus` with a terminal status:

```csharp
public enum MessageStatus
{
    Generating = 1,
    Completed = 2,
    Failed = 3,
    Stopped = 4
}
```

Add an aggregate operation:

```csharp
public ErrorOr<ChatMessage> StopAssistantMessage(
    ChatMessageId messageId,
    MessageContent? content,
    DateTimeOffset stoppedAt)
```

Rules:

- Target must exist.
- Target must be an assistant message.
- Target must currently be `Generating`.
- When partial content exists, it must pass the existing `MessageContent` validation.
- When no token text exists yet, `Content` may remain `null`.
- On success, set `Content`, set `Status = Stopped`, set `CompletedAt = stoppedAt`, and update the chat's `UpdatedAt`.

Stopped is terminal. Existing guards that reject only `Generating` should naturally allow stopped messages unless a feature has a specific reason to reject empty or partial assistant content.

---

## 5. Stop Signal

Add a small application seam, for example:

```csharp
public interface ITurnStopSignal
{
    Task RequestStopAsync(Guid assistantMessageId, CancellationToken cancellationToken);

    Task<bool> IsStopRequestedAsync(Guid assistantMessageId, CancellationToken cancellationToken);
}
```

The first implementation can be Redis-backed with a short TTL, keyed by assistant message id. Redis is only the cooperative signal. The durable truth is the later aggregate transition to `Stopped`.

This keeps the API endpoint and worker loosely coupled without introducing a new durable lifecycle table.

---

## 6. API

Add a FastEndpoints endpoint:

```text
POST /v1/chats/{chatId}/messages/{assistantMessageId}/stop
```

Flow:

1. Validate route ids.
2. Load the chat for the current user.
3. Verify the target message exists, belongs to the chat, is an assistant message, and is `Generating`.
4. Record the stop request through `ITurnStopSignal.RequestStopAsync`.
5. Return an accepted or no-content response.

The endpoint does not transition the message to `Stopped`, because it does not have the orchestrator's accumulated partial content. The worker performs the durable transition when it observes the signal.

FastEndpoints and the existing `Mediator` package family should be used. Do not introduce ASP.NET Core controllers or MediatR.

---

## 7. Orchestrator Flow

Inject `ITurnStopSignal` into `ChatTurnOrchestrator` as a seam.

During the agent event loop:

```csharp
await foreach (TurnEvent turnEvent in agentRunner.RunAsync(context, cancellationToken))
{
    if (turnEvent is TokenEvent token)
    {
        text.Append(token.Text);
    }

    await publisher.PublishAsync(turnEvent, cancellationToken);

    if (await stopSignal.IsStopRequestedAsync(assistantMessage.Id.Value, cancellationToken))
    {
        await StopTurnAsync(thread, assistantMessage, text.ToString(), cancellationToken);
        return;
    }
}
```

`StopTurnAsync` should:

1. Create `MessageContent` from the accumulated text when it is not empty.
2. Call `thread.StopAssistantMessage(...)`.
3. Save through `IUnitOfWork`.
4. Publish `StoppedEvent`.

If no token text has been produced, the message still transitions to `Stopped` with `Content = null`. This keeps the user-requested stop distinct from provider failure while preserving the rule that generated text, when present, is stored exactly as partial assistant content.

---

## 8. Stream Contract

Add a new append-only event:

```csharp
public sealed record StoppedEvent(Guid TurnId) : TurnEvent(TurnId);
```

Add the explicit discriminator `"stopped"` to `TurnEvent`.

Update Redis stream handling so terminal events are:

- `DoneEvent`
- `FailedEvent`
- `StoppedEvent`

The stream TTL behavior should match done and failed turns.

---

## 9. Races And Failure Handling

- **Stop after completion:** endpoint should report that the message is already terminal or no-op consistently with nearby endpoint conventions.
- **Stop while provider emits another token:** cooperative stop is best-effort. Tokens emitted before the signal is observed may be included.
- **Worker shutdown:** existing `OperationCanceledException` behavior remains. Shutdown leaves the message `Generating` so redelivery can restart the turn.
- **Redelivery after stop:** because stopped is terminal, the existing idempotency check should skip redelivered jobs whose assistant message is no longer `Generating`.
- **Redis stop signal lost:** the message remains generating until the worker finishes or fails. Redis is a request signal, not durable state.

---

## 10. Testing Notes

Before writing tests, confirm test scope with the user per `AGENTS.md`.

Likely coverage:

- Domain: stopping a generating assistant stores content and marks `Stopped`; stopping non-generating messages is rejected.
- Serializer: `StoppedEvent` round-trips with the `"stopped"` discriminator.
- Orchestrator: when the stop signal is observed after tokens, partial content is persisted and `StoppedEvent` is published.
- Stream reader/publisher: `StoppedEvent` terminates the stream and applies TTL.
- Endpoint: stop request validates ownership and generating assistant status.
