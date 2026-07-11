# Agent Runs Domain Layer — Design Spec

**Date:** 2026-07-11
**Status:** Approved design, pre-implementation
**Scope:** PR #1 of the agent-mode work — the generic `AgentRun` / `AgentRunActivity` domain concept plus its persistence. Fully inert: nothing calls it yet.

**Goal:** A kind-agnostic domain foundation for long-running agent features. Research is the first agent kind, but the aggregate, activity vocabulary, and schema must accept future kinds (coding agent, data analysis, scheduled summarizer, …) with **zero schema changes and zero edits to the generic model** — a new kind brings only a new `AgentRunKind` enum member and its own activity `type` strings.

**Relation to prior work:** Inspired by the research-mode spec in the `rabat` workspace (`2026-07-11-research-mode-design.md`). That spec's architecture rules are kept (see below), but its `AgentRun` was research-shaped despite carrying a `Kind` discriminator: `ResearchBrief Brief`, `CurrentPhase`, `SearchCount`, `SourceCount` columns, an `ActivityKind` of `Phase | Search | Read | Reasoning | Source`, and a `StartResearch` factory. Adding a second kind would have meant a migration and enum surgery — the discriminator was a lie. This spec fixes that. Activity vocabulary is additionally informed by boop-agent's `agentLogs` (execution-native `logType` + open `toolName`).

---

## 1. Approved Decisions

1. **Chat-anchored, no status.** Every run is 1:1 with an assistant `ChatMessage`; `MessageStatus` is the single lifecycle owner. `AgentRun` stays purely descriptive — no code path may consult it to gate a transition. `FinishedAt` is descriptive timing only. Non-chat execution contexts are out of scope and not pre-designed.
2. **Generic core + activity-derived state.** The run stores only facts common to every agent kind. Counters (searches, sources, …) are never stored on the run — they derive from the activity log at read time. Current phase derives from the latest `Phase` activity. Kind-specific shape lives in a validated JSON `Detail` payload on activities.
3. **Hybrid activity vocabulary.** A small closed `ActivityKind` enum of execution-native roles (every agent is an LLM loop, so these are generic by construction) plus an open, validated `ActivityType` string owned by each agent kind.
4. **PR #1 = domain + persistence.** Aggregate, value objects, errors, repository interface and implementation, EF configurations, one migration. No events, messaging, orchestrators, endpoints, or read models.
5. **Tests are domain unit tests only.** Project rule: no infrastructure or API tests. Persistence is verified by build, the generated migration, and manual runs.

## 2. Binding Architecture Rules (carried over)

- The assistant `ChatMessage` is the only turn lifecycle; `AgentRun` is descriptive, never authoritative.
- All state changes go through the aggregate; activities are append-only (no update/delete paths anywhere).
- `AgentRun` is its own aggregate root with its own repository and tables — loading a chat never loads run data.

## 3. Domain Model (`Chat.Domain/AgentRuns/`)

### 3.1 AgentRun (aggregate root)

```csharp
public sealed class AgentRun : AggregateRoot<AgentRunId>
{
    public ChatId ChatId { get; }                      // anchor
    public ChatMessageId AssistantMessageId { get; }   // unique; the card message
    public UserId UserId { get; }
    public AgentRunKind Kind { get; }                  // Research = 1
    public AgentTask Task { get; }                     // the instruction that started the run
    public LlmModelId LlmModelId { get; }              // user-selected model
    public TokenUsage Usage { get; private set; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public IReadOnlyCollection<AgentRunActivity> Activities { get; }

    public ActivityTitle? CurrentPhase { get; }        // computed: latest Phase activity's title

    public static AgentRun Start(AgentRunKind kind, ChatId chatId, ChatMessageId assistantMessageId,
        UserId userId, AgentTask task, LlmModelId llmModelId, DateTimeOffset startedAt);

    public ErrorOr<AgentRunActivity> AppendActivity(ActivitySequence sequence, ActivityKind kind,
        ActivityType type, ActivityTitle title, ActivityDetail? detail, DateTimeOffset occurredAt);

    public ErrorOr<Success> RecordUsage(TokenUsage delta);   // additive
    public ErrorOr<Success> Finish(DateTimeOffset finishedAt);
}
```

One generic `Start` factory — deliberately no per-kind factories (`StartResearch` was the rabat mistake).

**Invariants:**

- `AppendActivity` rejects a sequence at or below the highest persisted (`AgentRunErrors.StaleActivitySequence`) — callers treat it as a skip; this is what makes resume-time replays idempotent.
- `AppendActivity` and `RecordUsage` reject once `FinishedAt` is set (`AgentRunErrors.AlreadyFinished`).
- `Finish` rejects a second call (`AlreadyFinished`) and `finishedAt < StartedAt` (`FinishedBeforeStarted`).
- Activities are immutable after creation; only the aggregate creates them (internal factory).

### 3.2 AgentRunActivity (child entity, append-only)

```csharp
public sealed class AgentRunActivity : Entity<AgentRunActivityId>
{
    public AgentRunId RunId { get; }
    public ActivitySequence Sequence { get; }   // per-run, strictly increasing (gaps allowed)
    public ActivityKind Kind { get; }           // closed, execution-native
    public ActivityType Type { get; }           // open, kind-owned vocabulary
    public ActivityTitle Title { get; }         // human-readable display line
    public ActivityDetail? Detail { get; }      // validated JSON payload
    public DateTimeOffset OccurredAt { get; }
}
```

Activities are **semantic events**, not observability traces — tens of rows per run, existing to render the agent card. Research maps: phase change → `Phase`, search/read → `ToolCall` (`type: "web.search"` / `"web.read"`), reasoning summary → `Thought`, source captured → `Observation` (`type: "source"`), failed page read → `Error`.

### 3.3 Value objects

All follow the repo pattern: sealed record, private ctor, `ErrorOr<T> Create(...)` (trim + validate), `FromDatabase(...)` throwing `DomainException`, `New()` with `Guid.CreateVersion7()` for ids.

| Value object | Rules |
| --- | --- |
| `AgentRunId`, `AgentRunActivityId` | Guid v7; non-empty |
| `AgentRunKind` (enum) | `Research = 1`; append-only growth |
| `AgentTask` | non-empty, trimmed, max 32,768 (aligned with `MessageContent.MaxLength`) |
| `ActivitySequence` | integer > 0 |
| `ActivityKind` (enum) | `Phase = 1, Thought = 2, ToolCall = 3, Observation = 4, Error = 5` |
| `ActivityType` | non-empty, lowercase `[a-z0-9._-]`, max 100 (e.g. `"web.search"`) |
| `ActivityTitle` | non-empty, trimmed, max 300 |
| `ActivityDetail` | must parse as JSON, max 16,384 chars |
| `TokenUsage` | `InputTokens ≥ 0`, `OutputTokens ≥ 0`; `Add(TokenUsage)` returns the sum |

Plus `AgentRunErrors` — only the errors the aggregate itself returns (`StaleActivitySequence`, `AlreadyFinished`, `FinishedBeforeStarted`); `NotFound` arrives with the first handler that needs it — and:

```csharp
public interface IAgentRunRepository
{
    void Add(AgentRun run);

    Task<AgentRun?> GetByIdAsync(AgentRunId id, CancellationToken cancellationToken = default);

    Task<AgentRun?> GetByAssistantMessageIdAsync(ChatMessageId assistantMessageId, CancellationToken cancellationToken = default);
}
```

## 4. Persistence (`Chat.Infrastructure/AgentRuns/`)

Same `chat-db`; one EF migration applied by the existing `Chat.MigrationWorker`. Conventions match `ChatMessageConfiguration`: explicit snake_case table names, `HasConversion` + `FromDatabase` for VOs, enums stored as strings.

### 4.1 `agent_runs`

- PK `id`. Unique index on `assistant_message_id`. Index on `chat_id`.
- Columns: `chat_id`, `assistant_message_id`, `user_id`, `kind` (string), `task`, `llm_model_id`, `input_tokens` + `output_tokens` (`TokenUsage` as an EF complex property), `started_at`, `finished_at` (null).
- **Composite FK `(chat_id, assistant_message_id)` → `chat_messages` alternate key `(chat_id, id)`, cascade delete** — the same pattern `SharedChat` uses. The database guarantees the anchored message belongs to the anchored chat, and deleting the chat (or the message) removes the run.

### 4.2 `agent_run_activities`

- PK `id`. FK `run_id` → `agent_runs`, cascade delete. **Unique index `(run_id, sequence)`** — DB backstop for the idempotency invariant.
- Columns: `sequence` (int), `kind` (string), `type` (varchar 100), `title` (varchar 300), `detail` (`jsonb`, null), `occurred_at`.
- Append-only by convention; no update/delete code paths.

### 4.3 Repository

`internal sealed AgentRunRepository`; activities loaded ordered by `sequence`.

## 5. Testing (`tests/Chat/Chat.Domain.Tests/AgentRuns/`)

Plain xunit `Assert`, unit tests only:

- Every value object: `Create` happy path, each rejection, `FromDatabase` throw on invalid input.
- Aggregate: `Start` yields zero activities, zero usage, null `FinishedAt`; append assigns and orders activities; stale/duplicate sequence rejected; append and `RecordUsage` after `Finish` rejected; double `Finish` rejected; `finishedAt < StartedAt` rejected; usage accumulates across calls; `CurrentPhase` derives from the latest `Phase` activity and is null before any.

## 6. Out of Scope (later focused PRs)

`MessageKind` on `ChatMessage`; `TurnEvent` additions and stream contract; run orchestrator, job contract, messaging; workflow checkpoint store; start/stop endpoints; read models and activity endpoints; the MAF research workflow itself. Each is its own design/plan pass on top of this foundation.
