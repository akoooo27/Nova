# Agent Run Pipeline + Research Workflow — Design Spec

**Date:** 2026-07-12
**Status:** Approved design, pre-implementation
**Scope:** PR #2 of the agent-mode work — the generic agent-run execution pipeline plus the first concrete agent kind: the Microsoft Agent Framework (MAF) Workflows research agent. Runs work end-to-end after this PR; crash-resume is deferred to PR #3.

**Goal:** A ChatGPT-style deep research mode on top of the merged generic `AgentRun` domain (`2026-07-11-agent-runs-domain-design.md`). The user submits a research task; a dedicated queue delivers it to `Chat.TurnWorker`; a MAF workflow plans, searches, reads, and synthesizes a cited report; semantic progress streams live over Redis/SSE and persists durably as `AgentRunActivity` rows; the finished report is a normal assistant message. Everything mechanical is kind-agnostic — a second agent kind later adds only a start surface, a keyed runner, and its activity-type strings.

**Relation to prior work:**

- Supersedes the research-mode spec from the `rabat` workspace (`2026-07-11-research-mode-design.md`). Its architecture survives; its research-shaped persistence and events are replaced by the generic domain and the generic stream contract below. Where this spec is silent, rabat's decisions stand.
- Builds on `2026-07-11-agent-runs-domain-design.md` (PR #1: aggregate + persistence, merged) — this PR touches neither the aggregate nor its tables.
- Preserves the binding rules of `docs/superpowers/plans/2026-06-12-chat-turn-pipeline.md`, the stop semantics of `2026-06-24-stop-generation-design.md`, and the redelivery/delete-race semantics of `2026-07-07-turn-lifecycle-race-hardening-design.md`.

---

## 1. Approved Decisions

1. **Generic engine, research-shaped edges.** Stream event, job contract, orchestrator, queue, read model, and detail endpoint are kind-agnostic. Research owns only its start endpoints and its quarantined workflow runner. A future kind adds: an endpoint pair, one keyed runner registration, its `ActivityType` vocabulary — nothing else.
2. **Slicing: resume is PR #3.** This PR ships the full pipeline **and** the real MAF Workflows research runner, with restart-from-scratch redelivery semantics (today's turn-pipeline contract; safe because activity appends are sequence-idempotent). The `IWorkflowCheckpointStore` seam ships now with a no-op implementation (the `NoOpMemoryRetriever` pattern); PR #3 adds the Postgres store, MAF `CheckpointManager` resume, and the cleanup purge job without touching orchestrator logic.
3. **SSE ships here.** The never-implemented stream endpoint lands as an early task at `GET /v1/chats/{chatId}/turns/{turnId}/stream` — the exact path every `TurnStartedResponse.StreamPath` already advertises. It serves normal chat turns and agent runs alike.
4. **The report travels as a single final `TokenEvent`.** The runner emits activity/usage events during the run and one `TokenEvent` carrying the whole markdown report at the end. The orchestrator reuses the accumulate-text → `CompleteAssistantMessage` mechanic (including the empty-response guard) verbatim from `ChatTurnOrchestrator`, and SSE clients receive the report without a refetch.
5. **Carried over from rabat unchanged:** hard stop (no report, activities remain); no incremental report token streaming; dedicated start endpoints rather than a mode flag on SendMessage; ids-only job messages; dedicated low-concurrency queue.

## 2. Binding Architecture Rules (carried over — check every task against these)

- The assistant `ChatMessage` is the **only** run lifecycle. `AgentRun` stays descriptive; no code path consults it to gate a transition. All transitions go through `ChatThread` (`BeginAssistantMessage` / `CompleteAssistantMessage` / `FailAssistantMessage` / `StopAssistantMessage`).
- **Quarantine:** `Microsoft.Agents.AI.*` (now including `.Workflows`) types appear only under `Chat.Infrastructure/Agents/`. Everything else speaks `TurnEvent`.
- **`TurnEvent` is append-only.** One new event; no existing shape or discriminator changes.
- **Orchestrators are sequencing only.** New behavior enters through seams, never inline branching.
- **`IContextBuilder` is untouched.** Agent runs assemble their own context behind their own seam.
- Activities are append-only and created only by the `AgentRun` aggregate; the stale-sequence rejection is the idempotency backstop.

## 3. Scope

**In scope**

- `MessageKind` on `ChatMessage` (`Text`, `AgentRun`) + regenerate guard + migration.
- `AgentActivityEvent` stream addition; serializer support.
- SSE stream endpoint (all turns).
- `AgentRunRequested` job, dedicated queue/consumer in `Chat.TurnWorker`, `AgentRunOrchestrator`, `IAgentRunRunner`/`IAgentRunnerResolver`/`IAgentRunContextBuilder`/`IWorkflowCheckpointStore` seams.
- Start-research commands + endpoints (new chat, existing chat).
- MAF Workflows research runner (Planner → bounded Search/Read/Critic loop → Writer) with budgets.
- Read model: message `kind`, compact agent-run summary on chat detail, full agent-run detail endpoint.
- Stop via the existing endpoint/signal.

**Out of scope (PR #3+ or deferred)**

- Checkpoint persistence, resume-on-redelivery, checkpoint purge job (PR #3 — own design pass).
- Stop-and-synthesize, clarifying questions, report token streaming, regenerating agent cards, per-executor models, scheduled runs, memory, analytics decorator for runs, per-kind queues, `IsTemporary` research chats, activity logs on shared chats, MassTransit upgrade (stays 8.4.1).

## 4. Domain: MessageKind

```csharp
public enum MessageKind
{
    Text = 1,
    AgentRun = 2
}
```

- New `kind` column on `chat_messages` (stored as string, matching existing enum conventions); migration backfills existing rows to `Text`.
- `BeginAssistantMessage` gains `MessageKind kind = MessageKind.Text` — no existing call-site changes. The user message carrying the research task stays `Text`.
- `RegenerateAssistant` on a message of kind `AgentRun` returns `Chat.CannotRegenerateAgentRun` (one guard covers every current and future agent kind).
- Branch and remix deep copies preserve `Kind` (honest provenance). Copies get no `AgentRun` row; the read model returns a null summary and the client renders the report as plain content.
- Editing the task user message is unaffected — the edit spawns a sibling branch whose new turn is a normal text turn.

## 5. Stream Contract

One append-only addition, mirroring the domain activity 1:1:

```csharp
public sealed record AgentActivityEvent
(
    Guid TurnId,
    int Sequence,
    string Kind,        // ActivityKind member name: "Phase" | "Thought" | "ToolCall" | "Observation" | "Error"
    string Type,        // open ActivityType vocabulary, e.g. "web.search"
    string Title,
    string? DetailJson
) : TurnEvent(TurnId);   // discriminator "agent_activity"
```

- `Sequence` is assigned by the runner and is the dedup key: the orchestrator's durable append and the client's live-vs-durable merge both key on it.
- During a run: `AgentActivityEvent`s and `UsageEvent`s only — no incremental `TokenEvent`s. At completion: exactly one `TokenEvent` with the full report (decision 4; the orchestrator concatenates if a runner ever emits several), then `DoneEvent`. Terminals remain `DoneEvent` / `FailedEvent` / `StoppedEvent`; Redis TTL behavior unchanged.
- **Card rendering contract (SPA):** state = `message.kind` + `message.status` + summary + durable activities, with SSE as the live tail. On reload, render from `GET .../agent-run`, then attach SSE from the last seen position. Durable data alone is always sufficient; SSE is an optimization, never the source of truth.

## 6. Application Layer

New folders mirror the turn pipeline: `Chat.Application/AgentRuns/` and `Chat.Application/Abstractions/AgentRuns/`.

### 6.1 Job contract

```csharp
public sealed record AgentRunRequested(Guid ChatId, string UserId, Guid AssistantMessageId, Guid RunId);
```

Ids only; the worker loads all state from the database. Published via `IMessageBus` **before** `SaveChangesAsync` (bus outbox, no dual-write — same as `TurnRequested`).

### 6.2 AgentRunOrchestrator (sequencing only)

Mirrors `ChatTurnOrchestrator`, kind-agnostic:

1. Malformed ids / missing thread / missing message → log + return (ack; poison-proof).
2. Assistant message not `Generating` → return (idempotent redelivery after a terminal transition).
3. Load the `AgentRun` by `RunId`. Run missing, or `IAgentRunnerResolver` has no runner for `run.Kind` → `FailAssistantMessage` + `FailedEvent` + ack (plus `run.Finish` when the run exists). A stuck-`Generating` card is worse than a failed one.
4. `IWorkflowCheckpointStore.GetLatestAsync(runId)` → always null in this PR (no-op store) → `ITokenPublisher.ResetAsync` (fresh start clears any partial stream). PR #3 makes non-null checkpoints skip the reset and resume.
5. `IAgentRunContextBuilder.BuildAsync(thread, assistantMessage, run)` → error → fail path.
6. Create a linked `CancellationTokenSource` = worker token + `AgentRunOptions.MaxRunDuration` timer. Iterate `runner.RunAsync(context, checkpoint: null, linkedToken)`:
   - publish every event to `ITokenPublisher`;
   - `AgentActivityEvent` → parse `Kind`/VOs, `run.AppendActivity(...)` + `SaveChangesAsync` (durable progress advances with the run); stale-sequence rejection → skip, no save; an unparseable activity (runner bug) → log + skip, never kill the run over one bad row;
   - `UsageEvent` → `run.RecordUsage(...)` (saved with the next mutation or the terminal save);
   - `TokenEvent` → accumulate report text;
   - after each event, poll `ITurnStopSignal.IsStopRequestedAsync` (events are sparse) → on stop, cancel the linked CTS and enter the stop path.
7. **Stop:** `thread.StopAssistantMessage(messageId, content: null, stoppedAt)`, `run.Finish(...)`, `store.DeleteAllAsync(runId)`, save, publish `StoppedEvent`. Hard stop: no report; activities remain.
8. **Completion:** `MessageContent.Create(accumulated)` (empty → fail path), `CompleteAssistantMessage(report)`, `run.Finish(...)`, `store.DeleteAllAsync(runId)`, save, publish `DoneEvent` last.
9. **Agent failure** (runner throws): `FailAssistantMessage(reason)`, `run.Finish(...)`, `store.DeleteAllAsync(runId)`, save, publish `FailedEvent`, ack — semantic failures are never blind-retried.
10. **Cancellation triage** (`OperationCanceledException` caught): stop requested → stop path (7). Max-duration CTS fired and the worker token didn't → fail path (9) with an exceeded-budget reason. Otherwise worker shutdown → rethrow; the message stays `Generating`, redelivery restarts from scratch, and the stale-sequence skip makes the replayed appends harmless.

### 6.3 Seams

```csharp
public interface IAgentRunRunner
{
    IAsyncEnumerable<TurnEvent> RunAsync(AgentRunContext context, WorkflowCheckpoint? checkpoint, CancellationToken cancellationToken);
}

public interface IAgentRunnerResolver
{
    IAgentRunRunner? Resolve(AgentRunKind kind);   // keyed-DI lookup; null = no runner registered
}

public interface IAgentRunContextBuilder
{
    Task<ErrorOr<AgentRunContext>> BuildAsync(ChatThread thread, ChatMessage assistantMessage, AgentRun run, CancellationToken cancellationToken);
}

public interface IWorkflowCheckpointStore
{
    Task SaveAsync(Guid runId, string checkpointId, JsonElement state, CancellationToken cancellationToken);
    Task<WorkflowCheckpoint?> GetLatestAsync(Guid runId, CancellationToken cancellationToken);
    Task DeleteAllAsync(Guid runId, CancellationToken cancellationToken);
}

public sealed record WorkflowCheckpoint(string CheckpointId, JsonElement State);
```

- `AgentRunContext`: `RunId`, `TurnId` (= assistant message id), `ChatId`, `UserId`, `Kind`, `Task` (string), `ExternalModelId`, `PriorConversation` (`IReadOnlyList<TurnMessage>`, reusing the existing record — completed messages on the active branch above the task message, oldest first).
- `AgentRunContextBuilder` resolves the external model id via `ILlmProviderRepository` (same errors as `ContextBuilder`) and walks the branch. `IContextBuilder` itself is untouched.
- `NoOpWorkflowCheckpointStore` (returns null / does nothing) is the only store in this PR. Runners ignore a null checkpoint.
- Registration: `ResearchWorkflowRunner` is registered as a keyed scoped service under `AgentRunKind.Research`; the resolver wraps `GetKeyedService`.

### 6.4 Commands

`CreateResearchChatCommand(string Task, Guid LlmModelId, Guid? ProjectId = null)` and `StartResearchCommand(Guid ChatId, string Task, Guid LlmModelId)`, both returning `ErrorOr<TurnStartedResult>` (existing shape). One transaction each:

1. Validate VOs; `ModelUsability.EnsureUsableAsync`; for create, mirror `CreateChatHandler`'s title derivation and project handling (no `IsTemporary`, no `TurnGenerationOptions`).
2. Create thread with the task as first user message / `AddUserMessage(task)` (existing `ParentStillGenerating` guard naturally locks the branch).
3. `BeginAssistantMessage(..., kind: MessageKind.AgentRun)`.
4. `AgentRun.Start(AgentRunKind.Research, chatId, assistantMessageId, userId, AgentTask.Create(task), llmModelId, now)` + `IAgentRunRepository.Add`.
5. Publish `AgentRunRequested` before `SaveChangesAsync`.

## 7. Messaging & Worker

- Consumer lives in the existing `Chat.TurnWorker`; a one-line delegation to `AgentRunOrchestrator`.
- **Dedicated receive endpoint** (research must not starve the chat-turn consumer): `ConcurrentMessageLimit` from `AgentRunOptions.QueueConcurrency` (default 1). Same transient retry intervals as the turn consumer (they only cover exceptions thrown before the run streams — semantic failures are acked terminally by the orchestrator).
- **Long-run accommodation:** the queue sets `x-consumer-timeout` explicitly to 60 min, above `MaxRunDuration` (45 min default), so a run can never outlive the broker's ack window.
- `AgentRunOptions` (generic, bound in the worker): `MaxRunDuration` = 45 min, `QueueConcurrency` = 1.

## 8. Research Runner (quarantine: `Chat.Infrastructure/Agents/Research/`)

- New package `Microsoft.Agents.AI.Workflows`, version aligned with the existing 1.13.x MAF entries in `Directory.Packages.props`.
- `ResearchWorkflowRunner : IAgentRunRunner` builds and drives the workflow; a mapper converts workflow progress into `TurnEvent`s. MAF types never leave the folder.
- **Graph:** `PlannerExecutor` → bounded loop (`SearchExecutor` → `ReadExecutor` → `CriticExecutor`, up to `MaxRounds`) → `WriterExecutor`. All LLM calls use the user-selected model through the same OpenRouter client pattern as `AgentFrameworkRunner` (`AgentOptions.BaseUrl`/`ApiKey`).
- **Tool access:** `SearchExecutor`/`ReadExecutor` call the underlying client seams directly (`IWebSearchClient` / the Firecrawl URL-reader seam). The `IAgentTool` delegates exist for model function-calling; deterministic executor calls skip that indirection.
- **Research activity vocabulary** (the kind-owned `ActivityType` strings):

  | Moment | ActivityKind | ActivityType | Detail payload |
  | --- | --- | --- | --- |
  | Phase change (Planning / Searching / Reading / Writing) | `Phase` | `phase` | — |
  | Search issued | `ToolCall` | `web.search` | `{ query }` |
  | Page fetched | `ToolCall` | `web.read` | `{ url }` |
  | Source captured | `Observation` | `source` | `{ url, title, domain }` |
  | Reasoning/critique summary | `Thought` | `reasoning` | — |
  | Failed page read (non-fatal) | `Error` | `read.failed` | `{ url }` |

- **Budgets** (`ResearchOptions`, bound in infrastructure): `MaxRounds` = 3, `MaxSearches` = 12, `MaxSourcesToRead` = 10. Exhausting a budget ends the loop gracefully (proceed to Writer); only `MaxRunDuration` (orchestrator-owned) fails a run.
- Sequence numbers are assigned monotonically from workflow state.
- **Accepted restart caveat:** after a mid-run crash, the redelivered run re-executes from scratch and re-emits sequences from 1; appends at or below the persisted watermark are skipped, so the durable log can interleave two attempts' narratives. Rare, cosmetic, and eliminated by PR #3's checkpoint resume.

## 9. API & Read Model

- **SSE:** `GET /v1/chats/{chatId}/turns/{turnId}/stream` — owner-only load (404 otherwise), `text/event-stream`, `Last-Event-ID` replay via `ITurnStreamReader`, and synthetic terminal events for already-terminal messages: `Completed` → `done`, `Failed` → `failed`, `Stopped` → `stopped`. Injects repository/reader directly (accepted exception to the Mediator pipeline, per the turn-pipeline plan).
- **Start:** `POST /v1/chats/research` → 201, `POST /v1/chats/{chatId}/research` → 202. Request `{ task, llmModelId, projectId? }` (projectId on the create variant only). Response: existing `TurnStartedResponse` including `StreamPath`.
- **Detail:** `GET /v1/chats/{chatId}/messages/{messageId}/agent-run` — owner-only: `{ kind, task, currentPhase, startedAt, finishedAt, usage: { inputTokens, outputTokens }, activities: [{ sequence, kind, type, title, detail, occurredAt }] }` ordered by sequence. 404 when the message has no run (including branched/remixed copies).
- **Chat detail (`GetChat`):** `MessageResponse` gains `kind`. Messages of kind `AgentRun` gain a compact summary, Dapper-joined and **derived, never stored**: `{ kind, currentPhase, activityCounts: { "<type>": n, ... }, startedAt, finishedAt }` — counts grouped by `ActivityType` (e.g. `web.search: 12`, `source: 8`). Null for copies without a run.
- **Stop:** existing `POST /v1/chats/{chatId}/messages/{assistantMessageId}/stop` works unchanged — same signal, same lifecycle owner.
- **Shared chats:** snapshot exposes `kind` and report content only; activities and summaries are never shared.

## 10. Races & Failure Handling

- **Redelivery after terminal:** `MessageStatus` idempotency check acks and skips.
- **Worker shutdown mid-run:** restart from scratch (decision 2); stale-sequence skip keeps durable appends idempotent.
- **Chat deleted mid-run:** race-hardening spec semantics; cascades remove runs/activities; the orchestrator's saves observe the missing thread / concurrency conflict and abort quietly.
- **Stop after terminal:** existing endpoint semantics (no-op/conflict), unchanged.
- **Redis unavailable:** live events degrade; durable activities still advance; the card falls back to polling the detail endpoint.
- **Thread interaction during a run:** `ParentStillGenerating` blocks new sends on the active branch until terminal (matches ChatGPT).
- **Regenerate on an agent card:** rejected (`Chat.CannotRegenerateAgentRun`).
- **Run row missing / unknown kind at execution time:** fail the assistant message — never leave a card stuck `Generating`.

## 11. Configuration

- `AgentRunOptions` (worker): `MaxRunDuration` 45 min, `QueueConcurrency` 1.
- `ResearchOptions` (infrastructure): `MaxRounds` 3, `MaxSearches` 12, `MaxSourcesToRead` 10.
- Existing `Agent__ApiKey`, `Exa__ApiKey`, `Firecrawl__ApiKey` reused; no new secrets. One new package: `Microsoft.Agents.AI.Workflows`.

## 12. Testing

Project rule: **domain and application unit tests only** — no infrastructure, repository, or endpoint tests. Coverage:

- Domain: `MessageKind` defaults on create/reply/begin; `kind` parameter respected; regenerate guard; branch/remix copies preserve kind.
- Serializer: `agent_activity` round-trip with a stable discriminator alongside the existing vocabulary.
- Application: `AgentRunContextBuilder` (history walk, model resolution errors); `AgentRunOrchestrator` — happy path (activities appended and saved as events flow, report completed, `DoneEvent` last), stale-sequence skip, stop path (terminal `Stopped`, null content), agent-failure path, max-duration failure, idempotent-redelivery no-op, missing-run failure; both start commands (thread + `AgentRun` + outboxed job in one save, model/thread guards).

## 13. Explicitly Deferred

PR #3 (own design pass): `agent_workflow_checkpoints` table + Postgres store, MAF `CheckpointManager` adapter, orchestrator/runner resume path, CleanupWorker purge backstop. Later: everything in §3 out-of-scope, rabat §12's deferrals (stop-and-synthesize, clarifying questions, per-executor models, scheduled research, agent dashboards), and an analytics decorator for agent runs.
