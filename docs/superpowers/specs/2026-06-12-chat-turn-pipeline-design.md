# Chat Turn Pipeline — Design

**Date:** 2026-06-12
**Status:** Approved design, pre-implementation
**Scope:** Asynchronous chat turn execution — API accepts a message, a worker runs the LLM turn via Microsoft Agent Framework, events stream back to the client over SSE.

---

## ⚠️ Read This First — Architecture Rules (Binding)

These rules exist because a previous project (Semantic Kernel based) rotted into an unnavigable, tightly coupled mess. Every rule below is a direct countermeasure. They are **not suggestions**. Any implementation step — human or agent — that violates one of these must stop and re-read this section.

**Rule 1 — The Agent Framework is quarantined to one class.**
`Microsoft.Agents.AI` (Agent Framework) types may appear in exactly one file: `AgentFrameworkRunner`. No other class imports the framework. The rest of the system speaks `TurnEvent` — our vocabulary, not Microsoft's. If you find yourself adding a framework type to a method signature outside `AgentFrameworkRunner`, you are recreating the old disease. Stop.

**Rule 2 — The orchestrator stays dumb.**
`ChatTurnOrchestrator` is sequencing only: call seam A, call seam B, loop, save. It contains **no branching business logic**. Adding a new pipeline step means introducing a new interface (seam) and calling it — never adding `if`/`switch` logic inline. If the orchestrator exceeds ~30 lines of real code, it is wrong.

**Rule 3 — Cross-cutting concerns are decorators, never edits.**
Telemetry, retries, rate limiting, logging around the agent run — each is an `IAgentRunner` decorator registered in DI. You must be able to delete any cross-cutting feature by deleting one DI line. If removing a feature requires editing another class, the feature was implemented wrong.

**Rule 4 — `IContextBuilder` is not a junk drawer.**
It assembles: system prompt + thread history + (later) memories. When it needs a fourth kind of input, split it into composable parts instead of growing it. "Just one more parameter" is how prompt assembly becomes the file nobody understands.

**Rule 5 — Agent tools get the same quarantine.**
When tools are added (later), each tool is a plain class with a narrow constructor-injected dependency set, registered in one place. `AgentFrameworkRunner` is the only code that adapts tools to framework types. Tools never receive `DbContext` or "the service provider" — they receive the one reader/repository they need.

**Rule 6 — `TurnEvent` contract is append-only.**
New event types may be added. Existing event shapes are never changed or removed once the frontend consumes them — add a new event instead. The contract lives in one file and is serialized with explicit discriminators, not type names.

**Rule 7 — No new lifecycle entities.**
The turn lifecycle is the assistant `ChatMessage` in `Generating` status. Do not introduce a `Turn`/`Job`/`Execution` entity. `ChatThread` already owns the invariants (`CompleteAssistantMessage`, `Fail`, `CannotRegenerateWhileGenerating`). State transitions go through the aggregate — never raw SQL, never direct property sets from pipeline code.

**Rule 8 — Memory is deferred and stays a no-op.**
`IMemoryRetriever` exists as an interface with a no-op implementation. Do not implement retrieval, extraction, embeddings, or vector storage until a dedicated design session decides to. Any "while we're here, let's add memory" impulse is out of scope.

---

## 1. Decisions Made (and Why)

| Decision | Choice | Why |
|---|---|---|
| Turn job queue | **RabbitMQ + MassTransit** (not Redis Streams) | Already in the stack (pinned MassTransit 8.4.1, Aspire RabbitMQ, existing consumers). EF Core **outbox** makes "persist message + enqueue job" atomic — Redis `XADD` after `SaveChanges` has a dual-write gap. Retries, `_error` dead-letter queues, and concurrency limits are config, not hand-rolled `XAUTOCLAIM` code. |
| Token transport | **Per-turn Redis Stream** (not pub/sub, not RabbitMQ) | RabbitMQ is wrong for per-turn ephemeral fan-out at token granularity. Plain pub/sub loses events on late connect/reconnect. A short-lived stream (`turn:{assistantMessageId}`, TTL after completion) gives replay-from-offset for SSE `Last-Event-ID` support. |
| Worker topology | **Separate `Chat.TurnWorker` executable** | Turn throughput is LLM-bound; HTTP traffic is not. Scales independently. Same pattern as `Chat.MigrationWorker`: references `Chat.Infrastructure`, same `chatdb`, wired in AppHost. A worker is a separate *process* of the same bounded context — sharing the DbContext is correct here. |
| Turn identity | **`turnId` = assistant `ChatMessageId`** | The aggregate already models the lifecycle (`Generating → Completed/Failed`, `FailureReason`, `CompletedAt`). No new schema. |
| Agent runtime | **Microsoft Agent Framework**, behind `IAgentRunner` | Per Rule 1 — one adapter class, swappable. |
| Analytics | **PostHog** via `IAnalytics`, attached as `TelemetryAgentRunner` decorator | `$ai_generation` schema gives LLM cost/latency/model dashboards for free; `tool_used` custom events for tool analytics. Never in the hot path; removable by deleting one DI registration (Rule 3). |
| Memory | **Deferred** | Rule 8. |

## 2. End-to-End Flow

```
Client                 Chat.Api                RabbitMQ           Chat.TurnWorker              Redis
  │ POST /messages        │                       │                     │                        │
  ├──────────────────────►│ ChatThread:           │                     │                        │
  │                       │  + user message       │                     │                        │
  │                       │  + assistant msg      │                     │                        │
  │                       │    (Generating)       │                     │                        │
  │                       │ SaveChanges + outbox ─┼──► turn job ───────►│                        │
  │ 202 {assistantMsgId}  │   (one transaction)   │   (consumer)        │ Orchestrator:          │
  │◄──────────────────────┤                       │                     │  IMemoryRetriever(noop)│
  │                       │                       │                     │  IContextBuilder       │
  │ GET /turns/{id}/stream│                       │                     │  IAgentRunner ─► Agent │
  ├──────────────────────►│                       │                     │     Framework          │
  │                       │◄── XREAD turn:{id} ◄──┼─────────────────────┤  XADD each TurnEvent ─►│
  │ SSE: tool_call,       │    (from offset 0 or  │                     │                        │
  │      token×N, usage,  │     Last-Event-ID)    │                     │  Complete/Fail on      │
  │      done             │                       │                     │   aggregate, save      │
  │                       │                       │                     │  EXPIRE turn:{id}      │
```

- **Enqueue:** the API command handler mutates `ChatThread` and publishes the turn job through the MassTransit EF outbox in the same transaction. No turn job without a persisted message; no persisted `Generating` message without a job. The job message carries ids only (`ChatId`, assistant `ChatMessageId`, user id) — the worker loads state from the DB, never trusts payload state.
- **Execute:** the MassTransit consumer resolves `ChatTurnOrchestrator` (scoped). The orchestrator loads the thread, builds context, runs the agent, publishes every `TurnEvent` to the per-turn Redis Stream, accumulates text, then calls `CompleteAssistantMessage` (or `Fail`) on the aggregate and saves.
- **Stream:** Chat.Api's SSE endpoint reads the per-turn Redis Stream from offset 0 (or the client's `Last-Event-ID`), forwarding entries as SSE events until `done`. Late connects and reconnects replay missed events. After completion the stream gets a TTL (minutes); the persisted message is the durable record.
- **Recovery:** on page refresh, the client loads the thread; an assistant message with `Status == Generating` means "resubscribe to its stream." `Status == Failed` + `FailureReason` renders a retry affordance (maps to the existing regenerate path).

## 3. Contracts and Seams

### TurnEvent (one file, append-only — Rule 6)

```csharp
public abstract record TurnEvent(string TurnId);
public sealed record TokenEvent(string TurnId, string Text) : TurnEvent(TurnId);
public sealed record ToolCallEvent(string TurnId, string Tool, string ArgsJson) : TurnEvent(TurnId);
public sealed record ToolResultEvent(string TurnId, string Tool, string Summary) : TurnEvent(TurnId);
public sealed record UsageEvent(string TurnId, string Model, int InputTokens, int OutputTokens) : TurnEvent(TurnId);
public sealed record DoneEvent(string TurnId) : TurnEvent(TurnId);
public sealed record FailedEvent(string TurnId, string Reason) : TurnEvent(TurnId);
```

`FailedEvent` is added relative to the original sketch so the live client learns about failure without polling. Serialization uses an explicit string discriminator per event type.

### Seams

```csharp
public interface IAgentRunner      { IAsyncEnumerable<TurnEvent> RunAsync(TurnContext ctx, CancellationToken ct); }
public interface ITokenPublisher   { Task PublishAsync(TurnEvent evt, CancellationToken ct); }   // Redis Stream impl
public interface IContextBuilder   { Task<TurnContext> BuildAsync(TurnJob job, RetrievedMemories memories, CancellationToken ct); }
public interface IMemoryRetriever  { Task<RetrievedMemories> RetrieveAsync(TurnJob job, CancellationToken ct); } // no-op impl (Rule 8)
public interface IAnalytics        { void Capture(string distinctId, string eventName, Dictionary<string, object> props); } // PostHog impl
```

Each unit answers: what it does, how you use it, what it depends on. If a new class can't answer those in a sentence each, its boundary is wrong.

### Components

| Component | Project | Responsibility | Depends on |
|---|---|---|---|
| Send-message command handler | Chat.Application | Mutate aggregate, outbox-publish turn job | `IChatRepository`, `IMessageBus` |
| Turn job consumer | Chat.TurnWorker | Ack/retry semantics only; delegates immediately | `ChatTurnOrchestrator` |
| `ChatTurnOrchestrator` | Chat.Application | Pure sequencing (Rule 2) | the five seams above + `IChatRepository` |
| `AgentFrameworkRunner` | Chat.Infrastructure | Adapt Agent Framework streaming updates → `TurnEvent` (Rule 1) | Agent Framework |
| `TelemetryAgentRunner` | Chat.Infrastructure | Pass-through decorator; PostHog `$ai_generation` + `tool_used` after the run (Rule 3) | `IAgentRunner`, `IAnalytics` |
| `RedisStreamTokenPublisher` | Chat.Infrastructure | XADD events, EXPIRE on done/failed | Redis |
| SSE stream endpoint | Chat.Api | XREAD from offset / `Last-Event-ID`, forward as SSE | Redis |

### DI shape

```csharp
services.AddScoped<IAgentRunner, AgentFrameworkRunner>();
services.Decorate<IAgentRunner, TelemetryAgentRunner>();   // delete this line → PostHog gone (Rule 3)
```

(Scrutor-style decoration so future decorators — e.g., resilience — stack without edits.)

## 4. Error Handling

- **LLM/provider failure mid-turn:** orchestrator catches, calls `Fail(reason)` on the aggregate, publishes `FailedEvent`, expires the stream. The consumer acks (the failure is *recorded*, not retriable blindly — retrying a half-streamed turn duplicates tokens client-side).
- **Transient infra failure before the agent ran** (DB hiccup on load, Redis unavailable): throw → MassTransit retry policy (short exponential, small cap) → `_error` queue on exhaustion. Safe because nothing was streamed yet.
- **Worker crash mid-turn:** RabbitMQ redelivers. The consumer must be **idempotent on redelivery**: if the assistant message is no longer `Generating`, ack and exit; if it is `Generating`, restart the turn and `XTRIM`/recreate the stream so the client doesn't see duplicated tokens.
- **Cancellation/regeneration races:** all state transitions go through the aggregate, which already rejects invalid transitions (`CannotCompleteNonGenerating`, etc.). The pipeline trusts the aggregate's answer (Rule 7).
- **Capacity:** `ConcurrentMessageLimit` on the consumer caps in-flight LLM calls per worker replica. Scaling = adding replicas; RabbitMQ competing consumers distribute turns.

## 5. Testing Strategy

- **Domain:** already covered by aggregate tests; new transitions reuse them.
- **`AgentFrameworkRunner`:** unit-test the update→`TurnEvent` mapping with fabricated framework updates.
- **`TelemetryAgentRunner`:** unit-test with a fake inner runner + recording `IAnalytics`; assert pass-through ordering and captured props.
- **Orchestrator:** unit-test sequencing with fakes for every seam — happy path, agent-throws path, redelivery-idempotency path.
- **SSE endpoint:** integration-test replay-from-offset against Redis.
- Per project convention (AGENTS.md): confirm test scope with the user before writing tests.

## 6. Out of Scope

- Memory retrieval/extraction (Rule 8) — separate future design.
- Agent tools — `ToolCallEvent`/`ToolResultEvent` exist in the contract, but no tools ship in this phase; tool architecture follows Rule 5 when it arrives.
- Frontend SSE consumption details (`posthog-js` product analytics noted, designed separately).
- Title generation, memory-extraction follow-up jobs — the orchestrator's "enqueue follow-up" hook stays empty for now.

## 7. Checklist for Every Future Addition to This Pipeline

Before adding anything here, answer in writing (PR description or plan step):

1. Which **seam** does this live behind? (No seam → design one first.)
2. Does any framework type leak past `AgentFrameworkRunner`? (Rule 1)
3. Did the orchestrator gain logic? (Rule 2 — it must not)
4. Can this be removed by deleting one DI line or one file? (Rule 3)
5. Does `TurnEvent` change shape rather than grow? (Rule 6 — shape changes forbidden)
6. Is a new entity being added for state the aggregate already owns? (Rule 7)

If any answer is wrong, the change is not ready to implement.
