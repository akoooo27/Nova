# Agent Run Pipeline + Research Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** PR #2 of agent mode — the generic agent-run execution pipeline (MessageKind, `agent_activity` stream event, SSE endpoint, job/queue/orchestrator, seams, start endpoints, read model) plus the first concrete kind: a MAF Workflows deep-research agent, per `docs/superpowers/specs/2026-07-12-agent-run-pipeline-design.md`.

**Architecture:** The merged `AgentRun` aggregate stays untouched and descriptive; the assistant `ChatMessage` (`kind: AgentRun`) remains the only lifecycle. A dedicated MassTransit queue delivers `AgentRunRequested` to `Chat.TurnWorker`, where a kind-agnostic `AgentRunOrchestrator` resolves a runner by `run.Kind` (keyed DI), publishes every `TurnEvent` to the existing Redis stream, durably appends `AgentActivityEvent`s to the aggregate (sequence-idempotent), and completes the message with the report delivered as one final `TokenEvent`. Crash-resume is PR #3: `IWorkflowCheckpointStore` ships as a consumed no-op seam; redelivery restarts from scratch.

**Tech Stack:** .NET 10, Mediator.SourceGenerator, FastEndpoints, MassTransit 8.4.1 (pinned), EF Core + Npgsql + Dapper, StackExchange.Redis, Microsoft Agent Framework 1.13.x (`Microsoft.Agents.AI.*` + new `Microsoft.Agents.AI.Workflows`), ErrorOr, FluentValidation, xunit.

## Global Constraints

- **Quarantine:** `Microsoft.Agents.AI.*` (including `.Workflows`) types appear ONLY under `src/services/Chat/Chat.Infrastructure/Agents/`. Everything else speaks `TurnEvent`.
- **`TurnEvent` is append-only:** one new event (`agent_activity`); never change an existing shape or discriminator.
- **Orchestrators are sequencing only:** no business branching; new behavior = new seam.
- **All message state transitions go through `ChatThread`** (`BeginAssistantMessage`/`CompleteAssistantMessage`/`FailAssistantMessage`/`StopAssistantMessage`). `AgentRun` is descriptive, never authoritative — no code path may consult it to gate a transition.
- **`IContextBuilder` (turns) is untouched.** Agent runs get their own `IAgentRunContextBuilder`.
- **Activities are append-only**, created only by the aggregate; the stale-sequence rejection is the idempotency backstop — callers treat it as a skip.
- **Temporary chats never support agent runs** (spec decision 6) — domain guard, not handler logic.
- **Tests:** domain + application unit tests ONLY. No infrastructure, repository, or endpoint tests (project rule).
- MassTransit stays 8.4.1. Mediator.SourceGenerator, never MediatR. FastEndpoints, never controllers. Handlers/consumers/repositories `internal sealed`. VO factories return `ErrorOr<T>`. Named arguments and expression-style per surrounding code.
- Commit after every task; build must pass before each commit. Run all commands from the repo root.

## File Structure Overview

```
src/services/Chat/
  Chat.Domain/Chats/
    ValueObjects/MessageKind.cs                       (Task 1)
    Entities/ChatMessage.cs                           (Task 1: Kind property + factory/copy)
    ChatThread.cs, ChatErrors.cs                      (Task 1: kind param + 2 guards)
  Chat.Application/
    Turns/TurnEvent.cs, TurnEventSerializer.cs        (Task 3: agent_activity)
    Abstractions/AgentRuns/{AgentRunContext,IAgentRunRunner,IAgentRunnerResolver,
                            IWorkflowCheckpointStore,IAgentRunContextBuilder}.cs  (Tasks 5,6)
    AgentRuns/{AgentRunRequested,AgentRunOptions,NoOpWorkflowCheckpointStore,
               AgentRunContextBuilder,AgentRunOrchestrator}.cs                    (Tasks 5,6,8)
    AgentRuns/Errors/AgentRunOperationErrors.cs        (Task 5)
    AgentRuns/Queries/GetAgentRun/*                    (Task 13)
    Chats/Commands/{CreateResearchChat,StartResearch}/* (Task 7)
    Chats/Queries/GetChat/* (kind + summary)           (Task 12)
    SharedChats/Queries/GetPublicSharedChat/* (kind)   (Task 12)
  Chat.Infrastructure/
    Chats/Configurations/ChatMessageConfiguration.cs   (Task 2)
    Database/Migrations/<new> AddChatMessageKind       (Task 2)
    Chats/Readers/ChatDetailReader.cs                  (Task 12)
    SharedChats/Readers/PublicSharedChatReader.cs      (Task 12)
    AgentRuns/{AgentRunnerResolver.cs,
               Consumers/{AgentRunRequestedConsumer,AgentRunRequestedConsumerDefinition}.cs} (Task 9)
    Agents/Research/{ResearchActivityTypes,ResearchState,ResearchProgressEvents,
                     ResearchPrompts,ResearchWorkflowRunner}.cs                   (Tasks 10,11)
    Agents/Research/Executors/{Planner,Search,Read,Critic,Writer}Executor.cs      (Task 10)
    Options/ResearchOptions.cs                         (Task 10)
    DependencyInjection.cs                             (Tasks 9,11)
  Chat.TurnWorker/appsettings.json                     (Task 9)
  Chat.Api/Endpoints/Chats/
    StreamTurn/Endpoint.cs                             (Task 4)
    GetChat/{MessageResponse,ResponseMapper}.cs        (Task 12)
    GetAgentRun/Endpoint.cs                            (Task 13)
    CreateResearchChat/Endpoint.cs, StartResearch/Endpoint.cs (Task 14)
  Chat.Api/Endpoints/SharedChats/GetSharedChat/*       (Task 12: kind)
tests/Chat/
  Chat.Domain.Tests/Chats/MessageKindTests.cs          (Task 1)
  Chat.Application.Tests/AgentRuns/*                   (Tasks 3,6,7,8,13)
Directory.Packages.props                               (Task 10)
```

---

## Task 1: Domain — `MessageKind` + guards

`MessageKind { Text, AgentRun }` on `ChatMessage`; `BeginAssistantMessage` gains a defaulted `kind` parameter; regenerating an agent card and starting an agent run in a temporary chat are rejected in the domain; branch/remix copies preserve `Kind`.

**Files:**
- Create: `src/services/Chat/Chat.Domain/Chats/ValueObjects/MessageKind.cs`
- Modify: `src/services/Chat/Chat.Domain/Chats/Entities/ChatMessage.cs`
- Modify: `src/services/Chat/Chat.Domain/Chats/ChatThread.cs`
- Modify: `src/services/Chat/Chat.Domain/Chats/ChatErrors.cs`
- Test: `tests/Chat/Chat.Domain.Tests/Chats/MessageKindTests.cs`

**Interfaces:**
- Consumes: existing `ChatThread`/`ChatMessage` members shown below.
- Produces: `MessageKind { Text = 1, AgentRun = 2 }`; `ChatMessage.Kind` property; `ChatThread.BeginAssistantMessage(parentMessageId, llmModelId, createdAt, MessageKind kind = MessageKind.Text)`; errors `Chat.CannotRegenerateAgentRun` and `Chat.CannotStartAgentRunInTemporaryChat`. Tasks 2, 7, 12 rely on these exact names.

- [ ] **Step 1: Write the failing tests**

`tests/Chat/Chat.Domain.Tests/Chats/MessageKindTests.cs`:

```csharp
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Domain.Tests.Chats;

public sealed class MessageKindTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    private static ChatThread CreateThread(bool isTemporary = false) =>
        ChatThread.Create
        (
            userId: UserId.Create("auth0|user-1").Value,
            title: ChatTitle.Create("Research").Value,
            firstUserMessage: MessageContent.Create("Research the topic").Value,
            createdAt: Now,
            isTemporary: isTemporary
        );

    [Fact]
    public void Create_RootUserMessage_HasTextKind()
    {
        ChatThread thread = CreateThread();

        Assert.Equal(MessageKind.Text, thread.FindMessage(thread.CurrentMessageId)!.Kind);
    }

    [Fact]
    public void BeginAssistantMessage_DefaultsToTextKind()
    {
        ChatThread thread = CreateThread();

        ChatMessage assistant = thread.BeginAssistantMessage(thread.CurrentMessageId, LlmModelId.New(), Now).Value;

        Assert.Equal(MessageKind.Text, assistant.Kind);
    }

    [Fact]
    public void BeginAssistantMessage_WithAgentRunKind_SetsKind()
    {
        ChatThread thread = CreateThread();

        ChatMessage assistant = thread.BeginAssistantMessage
        (
            parentMessageId: thread.CurrentMessageId,
            llmModelId: LlmModelId.New(),
            createdAt: Now,
            kind: MessageKind.AgentRun
        ).Value;

        Assert.Equal(MessageKind.AgentRun, assistant.Kind);
    }

    [Fact]
    public void BeginAssistantMessage_AgentRunKindOnTemporaryChat_ReturnsCannotStartAgentRunInTemporaryChat()
    {
        ChatThread thread = CreateThread(isTemporary: true);

        ErrorOr<ChatMessage> result = thread.BeginAssistantMessage
        (
            parentMessageId: thread.CurrentMessageId,
            llmModelId: LlmModelId.New(),
            createdAt: Now,
            kind: MessageKind.AgentRun
        );

        Assert.True(result.IsError);
        Assert.Equal("Chat.CannotStartAgentRunInTemporaryChat", result.FirstError.Code);
    }

    [Fact]
    public void BeginAssistantMessage_TextKindOnTemporaryChat_StillAllowed()
    {
        ChatThread thread = CreateThread(isTemporary: true);

        ErrorOr<ChatMessage> result = thread.BeginAssistantMessage(thread.CurrentMessageId, LlmModelId.New(), Now);

        Assert.False(result.IsError);
    }

    [Fact]
    public void RegenerateAssistant_OnAgentRunMessage_ReturnsCannotRegenerateAgentRun()
    {
        ChatThread thread = CreateThread();

        ChatMessage assistant = thread.BeginAssistantMessage
        (
            parentMessageId: thread.CurrentMessageId,
            llmModelId: LlmModelId.New(),
            createdAt: Now,
            kind: MessageKind.AgentRun
        ).Value;

        thread.CompleteAssistantMessage(assistant.Id, MessageContent.Create("# Report").Value, Now);

        ErrorOr<ChatMessage> result = thread.RegenerateAssistant(assistant.Id, LlmModelId.New(), Now);

        Assert.True(result.IsError);
        Assert.Equal("Chat.CannotRegenerateAgentRun", result.FirstError.Code);
    }

    [Fact]
    public void BranchFrom_PreservesAgentRunKindOnCopies()
    {
        ChatThread thread = CreateThread();

        ChatMessage assistant = thread.BeginAssistantMessage
        (
            parentMessageId: thread.CurrentMessageId,
            llmModelId: LlmModelId.New(),
            createdAt: Now,
            kind: MessageKind.AgentRun
        ).Value;

        thread.CompleteAssistantMessage(assistant.Id, MessageContent.Create("# Report").Value, Now);

        ChatThread branch = ChatThread.BranchFrom(thread, assistant.Id, Now).Value;

        Assert.Contains(branch.Messages, message => message.Kind == MessageKind.AgentRun);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test tests/Chat/Chat.Domain.Tests --filter "FullyQualifiedName~MessageKindTests"`
Expected: build error — `MessageKind` not found.

- [ ] **Step 3: Create `MessageKind.cs`** (mirrors `MessageStatus.cs` style):

```csharp
namespace Chat.Domain.Chats.ValueObjects;

#pragma warning disable CA1008
public enum MessageKind
{
    Text = 1,
    AgentRun = 2
}
```

- [ ] **Step 4: Add the two errors to `ChatErrors.cs`** (append inside the class; naming follows `CannotBranchTemporaryChat`/`CannotShareTemporaryChat`):

```csharp
    public static Error CannotRegenerateAgentRun(ChatMessageId messageId) =>
        Error.Conflict
        (
            code: "Chat.CannotRegenerateAgentRun",
            description: $"Message '{messageId.Value}' is an agent run card and cannot be regenerated."
        );

    public static Error CannotStartAgentRunInTemporaryChat(ChatId chatId) =>
        Error.Conflict
        (
            code: "Chat.CannotStartAgentRunInTemporaryChat",
            description: $"Temporary chat '{chatId.Value}' cannot run agents."
        );
```

- [ ] **Step 5: Thread `Kind` through `ChatMessage.cs`**

1. Add the property after `Role`:

```csharp
    public MessageKind Kind { get; private set; }
```

2. Add a `MessageKind kind` parameter to the private constructor (after `MessageRole role`) and assign `Kind = kind;` alongside the other assignments.
3. In `CreateUserMessage`, pass `kind: MessageKind.Text` to the constructor call.
4. Change `CreateAssistantMessage` to accept and forward the kind:

```csharp
    internal static ChatMessage CreateAssistantMessage
    (
        ChatId chatId,
        ChatMessageId parentMessageId,
        LlmModelId llmModelId,
        DateTimeOffset createdAt,
        SiblingIndex siblingIndex,
        MessageKind kind = MessageKind.Text
    ) => new
    (
        id: ChatMessageId.New(),
        chatId: chatId,
        parentMessageId: parentMessageId,
        role: MessageRole.Assistant,
        kind: kind,
        content: null,
        llmModelId: llmModelId,
        status: MessageStatus.Generating,
        createdAt: createdAt,
        completedAt: null,
        siblingIndex: siblingIndex
    );
```

5. In `CopyForBranch`, pass `kind: Kind` to the constructor call (copies preserve provenance).

- [ ] **Step 6: Thread `kind` through `ChatThread.cs`**

1. `BeginAssistantMessage` — new defaulted parameter and the temporary-chat guard, placed before the parent lookup:

```csharp
    public ErrorOr<ChatMessage> BeginAssistantMessage
    (
        ChatMessageId parentMessageId,
        LlmModelId llmModelId,
        DateTimeOffset createdAt,
        MessageKind kind = MessageKind.Text
    )
    {
        if (kind == MessageKind.AgentRun && IsTemporary)
        {
            return ChatErrors.CannotStartAgentRunInTemporaryChat(Id);
        }

        ChatMessage? parent = FindMessage(parentMessageId);
        // ... existing body unchanged, except the CreateAssistantMessage call gains kind:
```

and inside the existing `ChatMessage.CreateAssistantMessage(...)` call add `kind: kind` as the last named argument.

2. `RegenerateAssistant` — add the guard directly after the existing `Status == MessageStatus.Generating` check:

```csharp
        if (target.Kind == MessageKind.AgentRun)
        {
            return ChatErrors.CannotRegenerateAgentRun(messageId);
        }
```

- [ ] **Step 7: Run the tests to verify they pass, then the whole domain suite**

Run: `dotnet test tests/Chat/Chat.Domain.Tests`
Expected: PASS (7 new tests, no regressions — existing call sites compile unchanged because `kind` is defaulted).

- [ ] **Step 8: Commit**

```bash
git add src/services/Chat/Chat.Domain tests/Chat/Chat.Domain.Tests
git commit -m "feat(chat): add message kind with agent-run regenerate and temporary-chat guards"
```

---

## Task 2: Infrastructure — `kind` column mapping + migration

**Files:**
- Modify: `src/services/Chat/Chat.Infrastructure/Chats/Configurations/ChatMessageConfiguration.cs`
- Create (generated): `src/services/Chat/Chat.Infrastructure/Database/Migrations/<timestamp>_AddChatMessageKind.cs`

**Interfaces:**
- Consumes: `ChatMessage.Kind` (Task 1).
- Produces: `chat_messages.kind` column (string, default `'Text'`) — Task 12's SQL reads it.

- [ ] **Step 1: Add the mapping** in `ChatMessageConfiguration.Configure`, next to the existing `Status` mapping:

```csharp
        builder.Property(x => x.Kind)
            .HasConversion<string>()
            .HasDefaultValue(MessageKind.Text)
            .IsRequired();
```

(`MessageKind` resolves via the existing `Chat.Domain.Chats.ValueObjects` using. The database default backfills every existing row to `Text` in the generated migration.)

- [ ] **Step 2: Generate the migration**

```bash
dotnet ef migrations add AddChatMessageKind \
  --project src/services/Chat/Chat.Infrastructure \
  --startup-project src/workers/Chat.MigrationWorker \
  --output-dir Database/Migrations
```

Expected: a migration whose `Up` contains a single `AddColumn<string>` on `chat_messages` with `nullable: false, defaultValue: "Text"`. If anything else appears in the migration, stop — unrelated model drift must be investigated, not committed.

- [ ] **Step 3: Build and verify no further drift**

```bash
dotnet build src/services/Chat/Chat.Infrastructure
dotnet ef migrations has-pending-model-changes \
  --project src/services/Chat/Chat.Infrastructure \
  --startup-project src/workers/Chat.MigrationWorker
```

Expected: build PASS; "No changes have been made to the model since the last migration."

- [ ] **Step 4: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure
git commit -m "feat(chat): persist message kind with text backfill migration"
```

---

## Task 3: Application — `AgentActivityEvent` stream contract

One append-only `TurnEvent` mirroring the domain activity 1:1. `Kind` carries the `ActivityKind` member name (`"Phase" | "Thought" | "ToolCall" | "Observation" | "Error"`); `Type` carries the open kind-owned vocabulary (e.g. `"web.search"`).

**Files:**
- Modify: `src/services/Chat/Chat.Application/Turns/TurnEvent.cs`
- Modify: `src/services/Chat/Chat.Application/Turns/TurnEventSerializer.cs`
- Test: `tests/Chat/Chat.Application.Tests/AgentRuns/AgentActivityEventSerializerTests.cs`

**Interfaces:**
- Produces: `AgentActivityEvent(Guid TurnId, int Sequence, string Kind, string Type, string Title, string? DetailJson)` with discriminator `"agent_activity"`. Tasks 8, 10, 11 construct and consume it with exactly these property names.

- [ ] **Step 1: Write the failing test**

`tests/Chat/Chat.Application.Tests/AgentRuns/AgentActivityEventSerializerTests.cs`:

```csharp
using Chat.Application.Turns;

namespace Chat.Application.Tests.AgentRuns;

public sealed class AgentActivityEventSerializerTests
{
    private static readonly Guid TurnId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void RoundTrips_WithStableDiscriminator()
    {
        AgentActivityEvent original = new
        (
            TurnId: TurnId,
            Sequence: 7,
            Kind: "ToolCall",
            Type: "web.search",
            Title: "Searching: EU battery regulation 2026",
            DetailJson: "{\"query\":\"EU battery regulation 2026\"}"
        );

        string json = TurnEventSerializer.Serialize(original);

        Assert.Contains("\"type\":\"agent_activity\"", json);
        Assert.Equal("agent_activity", TurnEventSerializer.EventName(original));
        Assert.Equal(original, TurnEventSerializer.Deserialize(json));
    }

    [Fact]
    public void RoundTrips_WithNullDetail()
    {
        AgentActivityEvent original = new(TurnId, Sequence: 1, Kind: "Phase", Type: "phase", Title: "Planning", DetailJson: null);

        Assert.Equal(original, TurnEventSerializer.Deserialize(TurnEventSerializer.Serialize(original)));
    }
}
```

- [ ] **Step 2: Run it to verify compile failure**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~AgentActivityEventSerializerTests"`
Expected: build error — `AgentActivityEvent` not found.

- [ ] **Step 3: Append the event to `TurnEvent.cs`** — add one `[JsonDerivedType]` attribute to the existing list on the abstract record:

```csharp
[JsonDerivedType(typeof(AgentActivityEvent), "agent_activity")]
```

and append the record at the end of the file:

```csharp
public sealed record AgentActivityEvent(
    Guid TurnId,
    int Sequence,
    string Kind,
    string Type,
    string Title,
    string? DetailJson
) : TurnEvent(TurnId);
```

Do NOT touch any existing derived record (append-only rule).

- [ ] **Step 4: Add the serializer arm** in `TurnEventSerializer.EventName`, before the discard arm:

```csharp
        AgentActivityEvent => "agent_activity",
```

- [ ] **Step 5: Run the tests to verify they pass, plus the existing serializer suite**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~SerializerTests"`
Expected: PASS (new tests + existing `TurnEventSerializerTests` untouched and green).

- [ ] **Step 6: Commit**

```bash
git add src/services/Chat/Chat.Application tests/Chat/Chat.Application.Tests
git commit -m "feat(chat): add agent_activity turn event"
```

---

## Task 4: API — SSE stream endpoint

The endpoint every `TurnStartedResponse.StreamPath` already advertises (`/v1/chats/{chatId}/turns/{turnId}/stream`) but which was never implemented. Serves normal chat turns and agent runs alike via the already-registered `ITurnStreamReader`. No unit tests (API surface; project rule) — verified in Task 15.

**Files:**
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/StreamTurn/Endpoint.cs`
- Possibly modify: `src/services/Chat/Chat.Api/Program.cs` (Step 3)

**Interfaces:**
- Consumes: `ITurnStreamReader.ReadAsync(Guid turnId, string? fromEntryId, ct)` → `TurnStreamEntry(string EntryId, TurnEvent Event)` (existing); `IChatRepository.GetByIdAsync`; `TurnEventSerializer`.
- Produces: `GET /v1/chats/{chatId}/turns/{turnId}/stream` — SSE with `Last-Event-ID` replay and synthetic terminal events.

- [ ] **Step 1: Create the endpoint**

Design notes baked in — do not change casually: authorizes by loading the thread for the authenticated user (404 otherwise — no leak); for an already-terminal turn it emits one synthetic terminal event (`done`/`failed`/`stopped`) because the Redis stream may have expired; injects repositories directly instead of Mediator (streaming does not fit the command pipeline — accepted, deliberate exception, same as the spec notes).

```csharp
using Chat.Application.Abstractions.Turns;
using Chat.Application.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using FastEndpoints;

using Shared.Application.Authentication;

namespace Chat.Api.Endpoints.Chats.StreamTurn;

internal sealed class Endpoint(IChatRepository chats, ITurnStreamReader streamReader, IUserContext userContext)
    : EndpointWithoutRequest
{
    public const string RouteName = "Chat.Chats.StreamTurn";

    public override void Configure()
    {
        Get("/chats/{chatId}/turns/{turnId}/stream");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Stream Turn Events")
                .WithDescription("Streams turn events (tokens, agent activities, usage, done/failed/stopped) for an assistant message as Server-Sent Events. Supports resume via the Last-Event-ID header.")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        ErrorOr<UserId> userId = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatId = ChatId.Create(Route<Guid>("chatId"));
        ErrorOr<ChatMessageId> turnId = ChatMessageId.Create(Route<Guid>("turnId"));

        if (userId.IsError || chatId.IsError || turnId.IsError)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        ChatThread? thread = await chats.GetByIdAsync(chatId.Value, userId.Value, ct);
        ChatMessage? message = thread?.FindMessage(turnId.Value);

        if (thread is null || message is null || message.Role != MessageRole.Assistant)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        HttpContext.Response.StatusCode = StatusCodes.Status200OK;
        HttpContext.Response.ContentType = "text/event-stream";
        HttpContext.Response.Headers.CacheControl = "no-cache";

        // Terminal turn: the stream may already have expired, so emit the terminal event directly.
        // The client refetches content through the read endpoints.
        if (message.Status != MessageStatus.Generating)
        {
            TurnEvent terminal = message.Status switch
            {
                MessageStatus.Completed => new DoneEvent(message.Id.Value),
                MessageStatus.Stopped => new StoppedEvent(message.Id.Value),
                _ => new FailedEvent(message.Id.Value, message.FailureReason?.Value ?? "The turn failed.")
            };

            await WriteEventAsync("terminal", terminal, ct);
            return;
        }

        string? lastEventId = HttpContext.Request.Headers["Last-Event-ID"].FirstOrDefault();

        try
        {
            await foreach (TurnStreamEntry entry in streamReader.ReadAsync(message.Id.Value, lastEventId, ct))
            {
                await WriteEventAsync(entry.EntryId, entry.Event, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — nothing to clean up; the worker owns the turn.
        }
    }

    private async Task WriteEventAsync(string entryId, TurnEvent turnEvent, CancellationToken ct)
    {
        string payload =
            $"id: {entryId}\n" +
            $"event: {TurnEventSerializer.EventName(turnEvent)}\n" +
            $"data: {TurnEventSerializer.Serialize(turnEvent)}\n\n";

        await HttpContext.Response.WriteAsync(payload, ct);
        await HttpContext.Response.Body.FlushAsync(ct);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/services/Chat/Chat.Api`
Expected: PASS.

- [ ] **Step 3: Verify the API host can construct `RedisTurnStreamReader`**

Open `src/services/Chat/Chat.Api/Program.cs` and check for a `builder.AddRedisClient("redis");` line (the reader needs `IConnectionMultiplexer`; the stop-signal likely already forced it). If absent, add it directly after the existing Redis registration (e.g. `builder.AddRedisDistributedCache("redis");`):

```csharp
builder.AddRedisClient("redis");
```

If present: nothing to do.

- [ ] **Step 4: Commit**

```bash
git add src/services/Chat/Chat.Api
git commit -m "feat(chat): add sse turn stream endpoint with terminal replay"
```

---

## Task 5: Application — agent-run contracts and seams

The kind-agnostic vocabulary of the pipeline: the job message, the run context, the runner/resolver/checkpoint seams (checkpoint store is a consumed no-op until PR #3), options, and operation errors. Pure types — no test cycle of its own beyond compilation; behavior is tested in Tasks 6–8.

**Files:**
- Create: `src/services/Chat/Chat.Application/AgentRuns/AgentRunRequested.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/AgentRuns/AgentRunContext.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/AgentRuns/IAgentRunRunner.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/AgentRuns/IAgentRunnerResolver.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/AgentRuns/IWorkflowCheckpointStore.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/AgentRuns/IAgentRunContextBuilder.cs`
- Create: `src/services/Chat/Chat.Application/AgentRuns/NoOpWorkflowCheckpointStore.cs`
- Create: `src/services/Chat/Chat.Application/AgentRuns/AgentRunOptions.cs`
- Create: `src/services/Chat/Chat.Application/AgentRuns/Errors/AgentRunOperationErrors.cs`

**Interfaces:**
- Consumes: `TurnMessage`/`TurnRole` from `Chat.Application.Abstractions.Turns`; `AgentRunKind` from `Chat.Domain.AgentRuns.ValueObjects`; `TurnEvent`.
- Produces (used verbatim by Tasks 6–11, 13, 14): every type below, with exactly these signatures.

- [ ] **Step 1: Create `AgentRunRequested.cs`** (ids only — the worker loads all state from the database):

```csharp
namespace Chat.Application.AgentRuns;

public sealed record AgentRunRequested(
    Guid ChatId,
    string UserId,
    Guid AssistantMessageId,
    Guid RunId
);
```

- [ ] **Step 2: Create `AgentRunContext.cs`**

```csharp
using Chat.Application.Abstractions.Turns;
using Chat.Domain.AgentRuns.ValueObjects;

namespace Chat.Application.Abstractions.AgentRuns;

/// <summary>
/// Everything a runner needs to execute one agent run. <see cref="PriorConversation"/> holds the
/// completed messages on the active branch ABOVE the task user message (the task itself travels
/// separately as <see cref="Task"/>), oldest first.
/// </summary>
public sealed record AgentRunContext
(
    Guid RunId,
    Guid TurnId,
    Guid ChatId,
    string UserId,
    AgentRunKind Kind,
    string Task,
    string ExternalModelId,
    IReadOnlyList<TurnMessage> PriorConversation
);
```

- [ ] **Step 3: Create `IWorkflowCheckpointStore.cs`**

```csharp
using System.Text.Json;

namespace Chat.Application.Abstractions.AgentRuns;

public sealed record WorkflowCheckpoint(string CheckpointId, JsonElement State);

/// <summary>
/// Checkpoint persistence seam for resumable agent runs. PR #2 ships only
/// <see cref="Chat.Application.AgentRuns.NoOpWorkflowCheckpointStore"/> (always empty);
/// PR #3 adds the Postgres store and the MAF resume path. The orchestrator already
/// consumes this seam so PR #3 never touches orchestration logic.
/// </summary>
public interface IWorkflowCheckpointStore
{
    Task SaveAsync(Guid runId, string checkpointId, JsonElement state, CancellationToken cancellationToken);

    Task<WorkflowCheckpoint?> GetLatestAsync(Guid runId, CancellationToken cancellationToken);

    Task DeleteAllAsync(Guid runId, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Create `IAgentRunRunner.cs` and `IAgentRunnerResolver.cs`**

```csharp
using Chat.Application.Turns;

namespace Chat.Application.Abstractions.AgentRuns;

/// <summary>
/// Executes one agent run and streams its progress as TurnEvents: AgentActivityEvents and
/// UsageEvents during the run, then the finished report as a single final TokenEvent
/// (spec decision 4). Implementations live in the infrastructure quarantine and are
/// resolved per <c>AgentRunKind</c> via keyed DI.
/// </summary>
public interface IAgentRunRunner
{
    IAsyncEnumerable<TurnEvent> RunAsync
    (
        AgentRunContext context,
        WorkflowCheckpoint? checkpoint,
        CancellationToken cancellationToken
    );
}
```

```csharp
using Chat.Domain.AgentRuns.ValueObjects;

namespace Chat.Application.Abstractions.AgentRuns;

public interface IAgentRunnerResolver
{
    /// <summary>Returns the runner registered for the kind, or null when none exists.</summary>
    IAgentRunRunner? Resolve(AgentRunKind kind);
}
```

- [ ] **Step 5: Create `IAgentRunContextBuilder.cs`** (implemented in Task 6):

```csharp
using Chat.Domain.AgentRuns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;

using ErrorOr;

namespace Chat.Application.Abstractions.AgentRuns;

public interface IAgentRunContextBuilder
{
    Task<ErrorOr<AgentRunContext>> BuildAsync
    (
        ChatThread thread,
        ChatMessage assistantMessage,
        AgentRun run,
        CancellationToken cancellationToken
    );
}
```

- [ ] **Step 6: Create `NoOpWorkflowCheckpointStore.cs`**

```csharp
using System.Text.Json;

using Chat.Application.Abstractions.AgentRuns;

namespace Chat.Application.AgentRuns;

/// <summary>
/// Deliberate no-op (spec decision 2, the NoOpMemoryRetriever pattern): PR #2 runs restart
/// from scratch on redelivery. Do NOT implement persistence here — PR #3 replaces this
/// registration with the Postgres store and the resume path.
/// </summary>
public sealed class NoOpWorkflowCheckpointStore : IWorkflowCheckpointStore
{
    public Task SaveAsync(Guid runId, string checkpointId, JsonElement state, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<WorkflowCheckpoint?> GetLatestAsync(Guid runId, CancellationToken cancellationToken) =>
        Task.FromResult<WorkflowCheckpoint?>(null);

    public Task DeleteAllAsync(Guid runId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
```

- [ ] **Step 7: Create `AgentRunOptions.cs`** (plain options class in Application so the orchestrator can take it without new package references; bound + validated in the worker DI in Task 9):

```csharp
using System.ComponentModel.DataAnnotations;

namespace Chat.Application.AgentRuns;

public sealed class AgentRunOptions
{
    public const string SectionName = "AgentRuns";

    /// <summary>Hard budget: a run exceeding this is failed (never resumed into a timeout loop).
    /// TimeSpan (config format "00:45:00") so tests can use millisecond budgets.</summary>
    public TimeSpan MaxRunDuration { get; init; } = TimeSpan.FromMinutes(45);

    /// <summary>In-flight agent runs per worker replica (dedicated queue's ConcurrentMessageLimit).</summary>
    [Range(1, 8)]
    public int QueueConcurrency { get; init; } = 1;
}
```

- [ ] **Step 8: Create `Errors/AgentRunOperationErrors.cs`**

```csharp
using Chat.Domain.Chats.ValueObjects;

using ErrorOr;

namespace Chat.Application.AgentRuns.Errors;

public static class AgentRunOperationErrors
{
    public static Error NotFound(ChatMessageId messageId) =>
        Error.NotFound
        (
            code: "AgentRun.NotFound",
            description: $"No agent run found for message '{messageId.Value}'."
        );
}
```

- [ ] **Step 9: Build**

Run: `dotnet build src/services/Chat/Chat.Application`
Expected: PASS.

- [ ] **Step 10: Commit**

```bash
git add src/services/Chat/Chat.Application
git commit -m "feat(agents): add agent run job contract, context, and pipeline seams"
```

---

## Task 6: Application — `AgentRunContextBuilder`

Resolves the run's external model id and walks the active branch for prior conversation. The turn pipeline's `IContextBuilder` is untouched (binding rule).

**Files:**
- Create: `src/services/Chat/Chat.Application/AgentRuns/AgentRunContextBuilder.cs`
- Create: `tests/Chat/Chat.Application.Tests/AgentRuns/FakeLlmProviderRepositoryForRuns.cs` — FIRST check `tests/Chat/Chat.Application.Tests` for an existing reusable `ILlmProviderRepository` fake (the ContextBuilder/FavoriteModels suites have one); if a seedable fake already exists and is `internal` in a reachable namespace, reuse it and skip this file.
- Test: `tests/Chat/Chat.Application.Tests/AgentRuns/AgentRunContextBuilderTests.cs`

**Interfaces:**
- Consumes: `ILlmProviderRepository.GetByModelIdAsync(LlmModelId, ct)` + `LlmProvider.FindModel(LlmModelId)`; `TurnErrors.ModelNotFound` (existing, from `Chat.Application.Turns`); `AgentRunContext` (Task 5); `TestCatalogFactory` (`Chat.Application.Tests.ModelCatalog`).
- Produces: `internal sealed AgentRunContextBuilder : IAgentRunContextBuilder` — registered in Task 9, consumed by Task 8.

- [ ] **Step 1: Write the failing tests**

```csharp
using Chat.Application.Abstractions.AgentRuns;
using Chat.Application.Abstractions.Turns;
using Chat.Application.AgentRuns;
using Chat.Application.Tests.ModelCatalog;
using Chat.Domain.AgentRuns;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.AgentRuns;

public sealed class AgentRunContextBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeLlmProviderRepositoryForRuns _providers = new();

    private (ChatThread Thread, ChatMessage Assistant, AgentRun Run) SeedRunInConversation(LlmModel model)
    {
        ChatThread thread = ChatThread.Create
        (
            userId: UserId.Create("auth0|user-1").Value,
            title: ChatTitle.Create("Hello").Value,
            firstUserMessage: MessageContent.Create("What is Redis?").Value,
            createdAt: Now
        );

        ChatMessage firstAssistant = thread.BeginAssistantMessage(thread.CurrentMessageId, model.Id, Now).Value;
        thread.CompleteAssistantMessage(firstAssistant.Id, MessageContent.Create("An in-memory store.").Value, Now);

        ChatMessage taskMessage = thread.AddUserMessage
        (
            parentMessageId: firstAssistant.Id,
            content: MessageContent.Create("Research Redis Streams adoption").Value,
            createdAt: Now
        ).Value;

        ChatMessage assistant = thread.BeginAssistantMessage
        (
            parentMessageId: taskMessage.Id,
            llmModelId: model.Id,
            createdAt: Now,
            kind: MessageKind.AgentRun
        ).Value;

        AgentRun run = AgentRun.Start
        (
            kind: AgentRunKind.Research,
            chatId: thread.Id,
            assistantMessageId: assistant.Id,
            userId: thread.UserId,
            task: AgentTask.Create("Research Redis Streams adoption").Value,
            llmModelId: model.Id,
            startedAt: Now
        );

        return (thread, assistant, run);
    }

    private LlmModel SeedModel()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        LlmModel model = provider.AddModel
        (
            externalModelId: TestCatalogFactory.CreateExternalModelId("gpt-4.1"),
            profile: TestCatalogFactory.CreateProfile()
        ).Value;
        _providers.Seed(provider);
        return model;
    }

    [Fact]
    public async Task BuildAsync_ProducesTaskModelAndPriorConversation_ExcludingTheTaskMessage()
    {
        LlmModel model = SeedModel();
        (ChatThread thread, ChatMessage assistant, AgentRun run) = SeedRunInConversation(model);

        AgentRunContextBuilder builder = new(_providers);

        ErrorOr<AgentRunContext> context = await builder.BuildAsync(thread, assistant, run, CancellationToken.None);

        Assert.False(context.IsError);
        Assert.Equal(assistant.Id.Value, context.Value.TurnId);
        Assert.Equal(run.Id.Value, context.Value.RunId);
        Assert.Equal(AgentRunKind.Research, context.Value.Kind);
        Assert.Equal("Research Redis Streams adoption", context.Value.Task);
        Assert.Equal("gpt-4.1", context.Value.ExternalModelId);

        Assert.Equal(2, context.Value.PriorConversation.Count);
        Assert.Equal(TurnRole.User, context.Value.PriorConversation[0].Role);
        Assert.Equal("What is Redis?", context.Value.PriorConversation[0].Text);
        Assert.Equal(TurnRole.Assistant, context.Value.PriorConversation[1].Role);
    }

    [Fact]
    public async Task BuildAsync_WhenModelUnknown_ReturnsModelNotFound()
    {
        LlmModel model = SeedModel();
        (ChatThread thread, ChatMessage assistant, AgentRun run) = SeedRunInConversation(model);

        AgentRunContextBuilder builder = new(new FakeLlmProviderRepositoryForRuns());

        ErrorOr<AgentRunContext> context = await builder.BuildAsync(thread, assistant, run, CancellationToken.None);

        Assert.True(context.IsError);
        Assert.Equal("Turn.ModelNotFound", context.FirstError.Code);
    }
}
```

- [ ] **Step 2: Create the provider fake** (skip if reusing an existing one — then update the test's type name accordingly). Open `src/services/Chat/Chat.Domain/ModelCatalog/ILlmProviderRepository.cs` and implement every member; only `GetByModelIdAsync` behaves, the rest `throw new NotSupportedException();`:

```csharp
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.ValueObjects;

namespace Chat.Application.Tests.AgentRuns;

internal sealed class FakeLlmProviderRepositoryForRuns : ILlmProviderRepository
{
    private readonly List<LlmProvider> _providers = [];

    public void Seed(LlmProvider provider) => _providers.Add(provider);

    public Task<LlmProvider?> GetByModelIdAsync(LlmModelId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_providers.FirstOrDefault(provider => provider.FindModel(id) is not null));

    // Every remaining ILlmProviderRepository member:
    //   => throw new NotSupportedException();
}
```

- [ ] **Step 3: Run to verify compile failure**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~AgentRunContextBuilderTests"`
Expected: build error — `AgentRunContextBuilder` not found.

- [ ] **Step 4: Create `AgentRunContextBuilder.cs`**

```csharp
using Chat.Application.Abstractions.AgentRuns;
using Chat.Application.Abstractions.Turns;
using Chat.Application.Turns;
using Chat.Domain.AgentRuns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;

using ErrorOr;

namespace Chat.Application.AgentRuns;

internal sealed class AgentRunContextBuilder(ILlmProviderRepository providers) : IAgentRunContextBuilder
{
    public async Task<ErrorOr<AgentRunContext>> BuildAsync
    (
        ChatThread thread,
        ChatMessage assistantMessage,
        AgentRun run,
        CancellationToken cancellationToken
    )
    {
        LlmProvider? provider = await providers.GetByModelIdAsync(run.LlmModelId, cancellationToken);
        LlmModel? model = provider?.FindModel(run.LlmModelId);

        if (provider is null || model is null)
        {
            return TurnErrors.ModelNotFound(run.LlmModelId);
        }

        // Prior conversation = completed exchanges ABOVE the task user message
        // (the task itself travels as run.Task), oldest first.
        List<TurnMessage> history = [];
        ChatMessage? taskMessage = assistantMessage.ParentMessageId is null
            ? null
            : thread.FindMessage(assistantMessage.ParentMessageId);
        ChatMessageId? cursor = taskMessage?.ParentMessageId;

        while (cursor is not null)
        {
            ChatMessage? message = thread.FindMessage(cursor);

            if (message is null)
            {
                break;
            }

            if (message.Content is not null && message.Status == MessageStatus.Completed)
            {
                history.Add(new TurnMessage
                (
                    Role: message.Role == MessageRole.User ? TurnRole.User : TurnRole.Assistant,
                    Text: message.Content.Value
                ));
            }

            cursor = message.ParentMessageId;
        }

        history.Reverse();

        return new AgentRunContext
        (
            RunId: run.Id.Value,
            TurnId: assistantMessage.Id.Value,
            ChatId: thread.Id.Value,
            UserId: thread.UserId.Value,
            Kind: run.Kind,
            Task: run.Task.Value,
            ExternalModelId: model.ExternalModelId.Value,
            PriorConversation: history
        );
    }
}
```

> Note: `TurnErrors.ModelNotFound` lives in `Chat.Application.Turns` — open it to confirm the member name before assuming; if the existing class exposes a different shape, reuse whatever the turn `ContextBuilder` uses for the same failure.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~AgentRunContextBuilderTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/services/Chat/Chat.Application tests/Chat/Chat.Application.Tests
git commit -m "feat(agents): add agent run context builder"
```

---

## Task 7: Application — start-research commands

`CreateResearchChatCommand` (new chat) and `StartResearchCommand` (existing chat). Both persist the task user message + a `Generating` assistant placeholder of kind `AgentRun` + the `AgentRun` row, and outbox-publish `AgentRunRequested` — one transaction. Mirrors `CreateChatHandler`/`SendMessageHandler`; research never carries `IsTemporary` or `TurnGenerationOptions`, and `requiresToolCalling: false` (research executors call the search/read clients directly — the model needs no function-calling support).

**Files:**
- Create: `src/services/Chat/Chat.Application/Chats/Commands/CreateResearchChat/CreateResearchChatCommand.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/CreateResearchChat/CreateResearchChatCommandValidator.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/CreateResearchChat/CreateResearchChatHandler.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/StartResearch/StartResearchCommand.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/StartResearch/StartResearchCommandValidator.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/StartResearch/StartResearchHandler.cs`
- Create: `tests/Chat/Chat.Application.Tests/AgentRuns/FakeAgentRunRepository.cs`
- Test: `tests/Chat/Chat.Application.Tests/AgentRuns/CreateResearchChatHandlerTests.cs`
- Test: `tests/Chat/Chat.Application.Tests/AgentRuns/StartResearchHandlerTests.cs`

**Interfaces:**
- Consumes: `AgentRun.Start(kind, chatId, assistantMessageId, userId, task, llmModelId, startedAt)`; `IAgentRunRepository.Add`; `AgentRunRequested` (Task 5); `MessageKind.AgentRun` (Task 1); existing `ModelUsability.EnsureUsableAsync(providers, modelId, cancellationToken, requiresToolCalling)`, `ChatOperationErrors`, `TurnStartedResult`, `IMessageBus`, `IUnitOfWork`, `IUserContext`, `IDateTimeProvider`; existing fakes `FakeChatRepository`, `FakeMessageBus` (`Chat.Application.Tests.Turns`), `FakeUserContext`, `FakeDateTimeProvider` (`Chat.Application.Tests.FavoriteModels`), `TurnFakeUnitOfWork` (`Chat.Application.Tests.Turns`), `TestCatalogFactory` (`Chat.Application.Tests.ModelCatalog`).
- Produces: both commands returning `ErrorOr<TurnStartedResult>`; `FakeAgentRunRepository` (reused by Tasks 8, 13). Task 14's endpoints send these commands.

- [ ] **Step 1: Create `FakeAgentRunRepository.cs`**

```csharp
using Chat.Domain.AgentRuns;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Domain.Chats.ValueObjects;

namespace Chat.Application.Tests.AgentRuns;

internal sealed class FakeAgentRunRepository : IAgentRunRepository
{
    private readonly List<AgentRun> _runs = [];

    public IReadOnlyList<AgentRun> Runs => _runs;

    public void Seed(AgentRun run) => _runs.Add(run);

    public Task<AgentRun?> GetByIdAsync(AgentRunId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_runs.FirstOrDefault(run => run.Id == id));

    public Task<AgentRun?> GetByAssistantMessageIdAsync(ChatMessageId assistantMessageId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_runs.FirstOrDefault(run => run.AssistantMessageId == assistantMessageId));

    public void Add(AgentRun run) => _runs.Add(run);
}
```

- [ ] **Step 2: Write the failing handler tests**

`CreateResearchChatHandlerTests.cs`:

```csharp
using Chat.Application.AgentRuns;
using Chat.Application.Chats.Commands.CreateResearchChat;
using Chat.Application.Chats.Results;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Tests.ModelCatalog;
using Chat.Application.Tests.Turns;
using Chat.Domain.AgentRuns;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;

using ErrorOr;

namespace Chat.Application.Tests.AgentRuns;

public sealed class CreateResearchChatHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeChatRepository _chats = new();
    private readonly FakeAgentRunRepository _runs = new();
    private readonly FakeLlmProviderRepositoryForRuns _providers = new();
    private readonly FakeMessageBus _bus = new();
    private readonly TurnFakeUnitOfWork _unitOfWork = new();

    private LlmModel SeedModel()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        LlmModel model = provider.AddModel
        (
            externalModelId: TestCatalogFactory.CreateExternalModelId(),
            profile: TestCatalogFactory.CreateProfile()
        ).Value;
        _providers.Seed(provider);
        return model;
    }

    private CreateResearchChatHandler CreateHandler() => new
    (
        userContext: new FakeUserContext("auth0|user-1"),
        chats: _chats,
        runs: _runs,
        providers: _providers,
        bus: _bus,
        unitOfWork: _unitOfWork,
        dateTimeProvider: new FakeDateTimeProvider(Now)
    );

    [Fact]
    public async Task Handle_PersistsThreadAgentCardAndRun_AndPublishesAgentRunRequested()
    {
        LlmModel model = SeedModel();

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new CreateResearchChatCommand("Research Redis Streams adoption", model.Id.Value), CancellationToken.None);

        Assert.False(result.IsError);

        ChatThread thread = Assert.Single(_chats.Threads);
        Assert.Equal(2, thread.Messages.Count);
        Assert.Contains(thread.Messages, m => m.Kind == MessageKind.AgentRun && m.Status == MessageStatus.Generating);

        AgentRun run = Assert.Single(_runs.Runs);
        Assert.Equal(AgentRunKind.Research, run.Kind);
        Assert.Equal("Research Redis Streams adoption", run.Task.Value);
        Assert.Equal(result.Value.AssistantMessageId, run.AssistantMessageId.Value);

        AgentRunRequested job = Assert.IsType<AgentRunRequested>(Assert.Single(_bus.Published));
        Assert.Equal(run.Id.Value, job.RunId);
        Assert.Equal(result.Value.AssistantMessageId, job.AssistantMessageId);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Handle_WhenModelUnknown_PublishesNothing()
    {
        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new CreateResearchChatCommand("Research something", Guid.CreateVersion7()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.LlmModelNotFound", result.FirstError.Code);
        Assert.Empty(_bus.Published);
        Assert.Empty(_runs.Runs);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }
}
```

`StartResearchHandlerTests.cs`:

```csharp
using Chat.Application.AgentRuns;
using Chat.Application.Chats.Commands.StartResearch;
using Chat.Application.Chats.Results;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Tests.ModelCatalog;
using Chat.Application.Tests.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.AgentRuns;

public sealed class StartResearchHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeChatRepository _chats = new();
    private readonly FakeAgentRunRepository _runs = new();
    private readonly FakeLlmProviderRepositoryForRuns _providers = new();
    private readonly FakeMessageBus _bus = new();
    private readonly TurnFakeUnitOfWork _unitOfWork = new();

    private LlmModel SeedModel()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        LlmModel model = provider.AddModel
        (
            externalModelId: TestCatalogFactory.CreateExternalModelId(),
            profile: TestCatalogFactory.CreateProfile()
        ).Value;
        _providers.Seed(provider);
        return model;
    }

    private ChatThread SeedThreadWithCompletedTurn(LlmModel model, bool isTemporary = false)
    {
        ChatThread thread = ChatThread.Create
        (
            userId: UserId.Create("auth0|user-1").Value,
            title: ChatTitle.Create("Hello").Value,
            firstUserMessage: MessageContent.Create("Hello").Value,
            createdAt: Now,
            isTemporary: isTemporary
        );

        ChatMessage assistant = thread.BeginAssistantMessage(thread.CurrentMessageId, model.Id, Now).Value;
        thread.CompleteAssistantMessage(assistant.Id, MessageContent.Create("Hi there!").Value, Now);

        _chats.Seed(thread);
        return thread;
    }

    private StartResearchHandler CreateHandler() => new
    (
        userContext: new FakeUserContext("auth0|user-1"),
        chats: _chats,
        runs: _runs,
        providers: _providers,
        bus: _bus,
        unitOfWork: _unitOfWork,
        dateTimeProvider: new FakeDateTimeProvider(Now)
    );

    [Fact]
    public async Task Handle_AppendsTaskAndAgentCard_AndPublishesAgentRunRequested()
    {
        LlmModel model = SeedModel();
        ChatThread thread = SeedThreadWithCompletedTurn(model);

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new StartResearchCommand(thread.Id.Value, "Research Redis Streams", model.Id.Value), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(4, thread.Messages.Count);
        Assert.Contains(thread.Messages, m => m.Kind == MessageKind.AgentRun && m.Status == MessageStatus.Generating);

        AgentRunRequested job = Assert.IsType<AgentRunRequested>(Assert.Single(_bus.Published));
        Assert.Equal(Assert.Single(_runs.Runs).Id.Value, job.RunId);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Handle_OnTemporaryChat_ReturnsCannotStartAgentRunInTemporaryChat()
    {
        LlmModel model = SeedModel();
        ChatThread thread = SeedThreadWithCompletedTurn(model, isTemporary: true);

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new StartResearchCommand(thread.Id.Value, "Research this", model.Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.CannotStartAgentRunInTemporaryChat", result.FirstError.Code);
        Assert.Empty(_bus.Published);
        Assert.Empty(_runs.Runs);
    }

    [Fact]
    public async Task Handle_WhileAssistantStillGenerating_ReturnsParentStillGenerating()
    {
        LlmModel model = SeedModel();

        ChatThread thread = ChatThread.Create
        (
            userId: UserId.Create("auth0|user-1").Value,
            title: ChatTitle.Create("Hello").Value,
            firstUserMessage: MessageContent.Create("Hello").Value,
            createdAt: Now
        );
        thread.BeginAssistantMessage(thread.CurrentMessageId, model.Id, Now);
        _chats.Seed(thread);

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new StartResearchCommand(thread.Id.Value, "Too eager", model.Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.ParentStillGenerating", result.FirstError.Code);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task Handle_WhenChatUnknown_ReturnsChatNotFound()
    {
        LlmModel model = SeedModel();

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new StartResearchCommand(Guid.CreateVersion7(), "Research this", model.Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.NotFound", result.FirstError.Code);
    }
}
```

- [ ] **Step 3: Run to verify compile failure**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~ResearchChatHandlerTests|FullyQualifiedName~StartResearchHandlerTests"`
Expected: build error — command types not found.

- [ ] **Step 4: Create the CreateResearchChat command, validator, handler**

`CreateResearchChatCommand.cs`:

```csharp
using Chat.Application.Chats.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.CreateResearchChat;

public sealed record CreateResearchChatCommand
(
    string Task,
    Guid LlmModelId,
    Guid? ProjectId = null
) : ICommand<ErrorOr<TurnStartedResult>>;
```

`CreateResearchChatCommandValidator.cs`:

```csharp
using Chat.Domain.AgentRuns.ValueObjects;

using FluentValidation;

namespace Chat.Application.Chats.Commands.CreateResearchChat;

internal sealed class CreateResearchChatCommandValidator : AbstractValidator<CreateResearchChatCommand>
{
    public CreateResearchChatCommandValidator()
    {
        RuleFor(x => x.Task)
            .NotEmpty()
            .MaximumLength(AgentTask.MaxLength);

        RuleFor(x => x.LlmModelId)
            .NotEmpty();
    }
}
```

`CreateResearchChatHandler.cs` (mirrors `CreateChatHandler` — same title derivation and project handling; adds the run + the agent-run job):

```csharp
using Chat.Application.Abstractions.Database;
using Chat.Application.AgentRuns;
using Chat.Application.Chats.Results;
using Chat.Domain.AgentRuns;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;
using Shared.Application.Messaging;

using SharedKernel;

namespace Chat.Application.Chats.Commands.CreateResearchChat;

internal sealed class CreateResearchChatHandler(
    IUserContext userContext,
    IChatRepository chats,
    IAgentRunRepository runs,
    ILlmProviderRepository providers,
    IMessageBus bus,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<CreateResearchChatCommand, ErrorOr<TurnStartedResult>>
{
    public async ValueTask<ErrorOr<TurnStartedResult>> Handle(CreateResearchChatCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<MessageContent> contentResult = MessageContent.Create(command.Task);
        ErrorOr<AgentTask> taskResult = AgentTask.Create(command.Task);
        ErrorOr<LlmModelId> modelIdResult = LlmModelId.Create(command.LlmModelId);

        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
        }

        if (contentResult.IsError)
        {
            errors.AddRange(contentResult.Errors);
        }

        if (taskResult.IsError)
        {
            errors.AddRange(taskResult.Errors);
        }

        if (modelIdResult.IsError)
        {
            errors.AddRange(modelIdResult.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        UserId userId = userIdResult.Value;
        MessageContent content = contentResult.Value;
        LlmModelId modelId = modelIdResult.Value;

        ErrorOr<Success> usabilityResult = await ModelUsability.EnsureUsableAsync
        (
            providers: providers,
            modelId: modelId,
            cancellationToken: cancellationToken,
            requiresToolCalling: false
        );

        if (usabilityResult.IsError)
        {
            return usabilityResult.Errors;
        }

        string titleSource = content.Value.Length <= ChatTitle.MaxLength
            ? content.Value
            : content.Value[..ChatTitle.MaxLength];

        ErrorOr<ChatTitle> titleResult = ChatTitle.Create(titleSource);

        if (titleResult.IsError)
        {
            return titleResult.Errors;
        }

        DateTimeOffset now = dateTimeProvider.UtcNow;

        ChatThread thread = ChatThread.Create
        (
            userId: userId,
            title: titleResult.Value,
            firstUserMessage: content,
            createdAt: now
        );

        if (command.ProjectId is not null)
        {
            ErrorOr<ProjectId> projectIdResult = ProjectId.Create(command.ProjectId.Value);

            if (projectIdResult.IsError)
            {
                return projectIdResult.Errors;
            }

            thread.MoveToProject(projectIdResult.Value, now);
        }

        ChatMessageId userMessageId = thread.CurrentMessageId;

        ErrorOr<ChatMessage> assistantMessageResult = thread.BeginAssistantMessage
        (
            parentMessageId: userMessageId,
            llmModelId: modelId,
            createdAt: now,
            kind: MessageKind.AgentRun
        );

        if (assistantMessageResult.IsError)
        {
            return assistantMessageResult.Errors;
        }

        ChatMessageId assistantMessageId = assistantMessageResult.Value.Id;

        AgentRun run = AgentRun.Start
        (
            kind: AgentRunKind.Research,
            chatId: thread.Id,
            assistantMessageId: assistantMessageId,
            userId: userId,
            task: taskResult.Value,
            llmModelId: modelId,
            startedAt: now
        );

        chats.Add(thread);
        runs.Add(run);

        AgentRunRequested job = new
        (
            ChatId: thread.Id.Value,
            UserId: userId.Value,
            AssistantMessageId: assistantMessageId.Value,
            RunId: run.Id.Value
        );

        // Published BEFORE SaveChangesAsync on purpose: the MassTransit bus outbox buffers
        // this and writes it to the outbox table inside the same transaction (no dual-write).
        await bus.PublishAsync(job, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new TurnStartedResult
        (
            ChatId: thread.Id.Value,
            UserMessageId: userMessageId.Value,
            AssistantMessageId: assistantMessageId.Value
        );
    }
}
```

- [ ] **Step 5: Create the StartResearch command, validator, handler**

`StartResearchCommand.cs`:

```csharp
using Chat.Application.Chats.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.StartResearch;

public sealed record StartResearchCommand
(
    Guid ChatId,
    string Task,
    Guid LlmModelId
) : ICommand<ErrorOr<TurnStartedResult>>;
```

`StartResearchCommandValidator.cs`:

```csharp
using Chat.Domain.AgentRuns.ValueObjects;

using FluentValidation;

namespace Chat.Application.Chats.Commands.StartResearch;

internal sealed class StartResearchCommandValidator : AbstractValidator<StartResearchCommand>
{
    public StartResearchCommandValidator()
    {
        RuleFor(x => x.ChatId)
            .NotEmpty();

        RuleFor(x => x.Task)
            .NotEmpty()
            .MaximumLength(AgentTask.MaxLength);

        RuleFor(x => x.LlmModelId)
            .NotEmpty();
    }
}
```

`StartResearchHandler.cs`:

```csharp
using Chat.Application.Abstractions.Database;
using Chat.Application.AgentRuns;
using Chat.Application.Chats.Errors;
using Chat.Application.Chats.Results;
using Chat.Domain.AgentRuns;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;
using Shared.Application.Messaging;

using SharedKernel;

namespace Chat.Application.Chats.Commands.StartResearch;

internal sealed class StartResearchHandler(
    IUserContext userContext,
    IChatRepository chats,
    IAgentRunRepository runs,
    ILlmProviderRepository providers,
    IMessageBus bus,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<StartResearchCommand, ErrorOr<TurnStartedResult>>
{
    public async ValueTask<ErrorOr<TurnStartedResult>> Handle(StartResearchCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(command.ChatId);
        ErrorOr<MessageContent> contentResult = MessageContent.Create(command.Task);
        ErrorOr<AgentTask> taskResult = AgentTask.Create(command.Task);
        ErrorOr<LlmModelId> modelIdResult = LlmModelId.Create(command.LlmModelId);

        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
        }

        if (chatIdResult.IsError)
        {
            errors.AddRange(chatIdResult.Errors);
        }

        if (contentResult.IsError)
        {
            errors.AddRange(contentResult.Errors);
        }

        if (taskResult.IsError)
        {
            errors.AddRange(taskResult.Errors);
        }

        if (modelIdResult.IsError)
        {
            errors.AddRange(modelIdResult.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        UserId userId = userIdResult.Value;
        ChatId chatId = chatIdResult.Value;
        LlmModelId modelId = modelIdResult.Value;

        ErrorOr<Success> usabilityResult = await ModelUsability.EnsureUsableAsync
        (
            providers: providers,
            modelId: modelId,
            cancellationToken: cancellationToken,
            requiresToolCalling: false
        );

        if (usabilityResult.IsError)
        {
            return usabilityResult.Errors;
        }

        ChatThread? thread = await chats.GetByIdAsync(chatId, userId, cancellationToken);

        if (thread is null)
        {
            return ChatOperationErrors.ChatNotFound(chatId);
        }

        DateTimeOffset now = dateTimeProvider.UtcNow;

        ErrorOr<ChatMessage> userMessageResult = thread.AddUserMessage
        (
            parentMessageId: thread.CurrentMessageId,
            content: contentResult.Value,
            createdAt: now
        );

        if (userMessageResult.IsError)
        {
            return userMessageResult.Errors;
        }

        ErrorOr<ChatMessage> assistantMessageResult = thread.BeginAssistantMessage
        (
            parentMessageId: userMessageResult.Value.Id,
            llmModelId: modelId,
            createdAt: now,
            kind: MessageKind.AgentRun
        );

        if (assistantMessageResult.IsError)
        {
            return assistantMessageResult.Errors;
        }

        ChatMessageId assistantMessageId = assistantMessageResult.Value.Id;

        AgentRun run = AgentRun.Start
        (
            kind: AgentRunKind.Research,
            chatId: thread.Id,
            assistantMessageId: assistantMessageId,
            userId: userId,
            task: taskResult.Value,
            llmModelId: modelId,
            startedAt: now
        );

        runs.Add(run);

        AgentRunRequested job = new
        (
            ChatId: thread.Id.Value,
            UserId: userId.Value,
            AssistantMessageId: assistantMessageId.Value,
            RunId: run.Id.Value
        );

        // Published BEFORE SaveChangesAsync on purpose (bus outbox, no dual-write).
        await bus.PublishAsync(job, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new TurnStartedResult
        (
            ChatId: thread.Id.Value,
            UserMessageId: userMessageResult.Value.Id.Value,
            AssistantMessageId: assistantMessageId.Value
        );
    }
}
```

> Note on the temporary-chat test: `StartResearchHandler` contains no temporary-chat check — the rejection comes from the domain guard inside `BeginAssistantMessage` (Task 1). If the test fails, fix the domain, not the handler. Note the failure ordering that implies: on a temporary chat the task user message is added before the guard fires, but nothing persists because the handler returns before `SaveChangesAsync`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~ResearchChatHandlerTests|FullyQualifiedName~StartResearchHandlerTests"`
Expected: PASS (6 tests).

- [ ] **Step 7: Commit**

```bash
git add src/services/Chat/Chat.Application tests/Chat/Chat.Application.Tests
git commit -m "feat(agents): add start-research commands with outboxed agent run jobs"
```

---

## Task 8: Application — `AgentRunOrchestrator`

The whole worker-side sequence, kind-agnostic, sequencing only. Error contract (spec §6.2 — do not deviate):

- Malformed job / missing thread / missing message → log + return (ack; poison-proof).
- Message no longer `Generating` → return (idempotent redelivery).
- Run row missing / no runner for kind / context error → **fail the message** (a stuck-`Generating` card is worse than a failed one).
- Runner exception → fail + `Finish` + `FailedEvent` + ack (never blind-retry).
- Stop poll per event → hard stop (`Stopped`, null content, activities remain).
- Max-duration cancellation → fail (never resumes into a timeout loop).
- Worker-shutdown `OperationCanceledException` → rethrow; redelivery restarts from scratch; stale-sequence skip makes replays harmless.
- Report = accumulated `TokenEvent` text, truncated to `MessageContent.MaxLength`; empty → fail.

**Files:**
- Create: `src/services/Chat/Chat.Application/AgentRuns/AgentRunOrchestrator.cs`
- Create: `tests/Chat/Chat.Application.Tests/AgentRuns/FakeAgentRunRunner.cs`
- Create: `tests/Chat/Chat.Application.Tests/AgentRuns/FakeAgentRunnerResolver.cs`
- Create: `tests/Chat/Chat.Application.Tests/AgentRuns/FakeAgentRunContextBuilder.cs`
- Test: `tests/Chat/Chat.Application.Tests/AgentRuns/AgentRunOrchestratorTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1, 3, 5, 6; existing `ITokenPublisher`, `ITurnStopSignal`, `IUnitOfWork`, `IDateTimeProvider`; existing test fakes `RecordingTokenPublisher`, `FakeTurnStopSignal`, `TurnFakeUnitOfWork` (`Chat.Application.Tests.Turns`), `FakeDateTimeProvider` (`Chat.Application.Tests.FavoriteModels`), `FakeChatRepository`, `FakeAgentRunRepository`.
- Produces: `public sealed partial class AgentRunOrchestrator` with `Task RunAsync(AgentRunRequested job, CancellationToken cancellationToken)` — Task 9's consumer calls exactly this.

- [ ] **Step 1: Create the fakes**

`FakeAgentRunRunner.cs`:

```csharp
using System.Runtime.CompilerServices;

using Chat.Application.Abstractions.AgentRuns;
using Chat.Application.Turns;

namespace Chat.Application.Tests.AgentRuns;

internal sealed class FakeAgentRunRunner(
    Func<AgentRunContext, CancellationToken, IAsyncEnumerable<TurnEvent>> script) : IAgentRunRunner
{
    public WorkflowCheckpoint? LastCheckpoint { get; private set; }

    public IAsyncEnumerable<TurnEvent> RunAsync
    (
        AgentRunContext context,
        WorkflowCheckpoint? checkpoint,
        CancellationToken cancellationToken
    )
    {
        LastCheckpoint = checkpoint;

        return script(context, cancellationToken);
    }

    public static async IAsyncEnumerable<TurnEvent> Script
    (
        TurnEvent[] events,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        foreach (TurnEvent turnEvent in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return turnEvent;
        }
    }

    public static async IAsyncEnumerable<TurnEvent> EventsThenThrow
    (
        TurnEvent[] events,
        Exception exception,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (TurnEvent turnEvent in Script(events, cancellationToken))
        {
            yield return turnEvent;
        }

        throw exception;
    }

    public static async IAsyncEnumerable<TurnEvent> Hang
    (
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        yield break;
    }
}
```

`FakeAgentRunnerResolver.cs`:

```csharp
using Chat.Application.Abstractions.AgentRuns;
using Chat.Domain.AgentRuns.ValueObjects;

namespace Chat.Application.Tests.AgentRuns;

internal sealed class FakeAgentRunnerResolver(IAgentRunRunner? runner) : IAgentRunnerResolver
{
    public IAgentRunRunner? Resolve(AgentRunKind kind) => runner;
}
```

`FakeAgentRunContextBuilder.cs`:

```csharp
using Chat.Application.Abstractions.AgentRuns;
using Chat.Application.Abstractions.Turns;
using Chat.Domain.AgentRuns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;

using ErrorOr;

namespace Chat.Application.Tests.AgentRuns;

internal sealed class FakeAgentRunContextBuilder : IAgentRunContextBuilder
{
    public Task<ErrorOr<AgentRunContext>> BuildAsync
    (
        ChatThread thread,
        ChatMessage assistantMessage,
        AgentRun run,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult<ErrorOr<AgentRunContext>>(new AgentRunContext
        (
            RunId: run.Id.Value,
            TurnId: assistantMessage.Id.Value,
            ChatId: thread.Id.Value,
            UserId: thread.UserId.Value,
            Kind: run.Kind,
            Task: run.Task.Value,
            ExternalModelId: "gpt-4.1",
            PriorConversation: []
        ));
}
```

- [ ] **Step 2: Write the failing orchestrator tests**

```csharp
using Chat.Application.Abstractions.AgentRuns;
using Chat.Application.AgentRuns;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Tests.Turns;
using Chat.Application.Turns;
using Chat.Domain.AgentRuns;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using Microsoft.Extensions.Logging.Abstractions;

namespace Chat.Application.Tests.AgentRuns;

public sealed class AgentRunOrchestratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeChatRepository _chats = new();
    private readonly FakeAgentRunRepository _runs = new();
    private readonly RecordingTokenPublisher _publisher = new();
    private readonly FakeTurnStopSignal _stopSignal = new();
    private readonly TurnFakeUnitOfWork _unitOfWork = new();

    private (ChatThread Thread, ChatMessage Assistant, AgentRun Run, AgentRunRequested Job) SeedPendingRun()
    {
        ChatThread thread = ChatThread.Create
        (
            userId: UserId.Create("auth0|user-1").Value,
            title: ChatTitle.Create("Research").Value,
            firstUserMessage: MessageContent.Create("Research Redis Streams").Value,
            createdAt: Now
        );

        ChatMessage assistant = thread.BeginAssistantMessage
        (
            parentMessageId: thread.CurrentMessageId,
            llmModelId: LlmModelId.New(),
            createdAt: Now,
            kind: MessageKind.AgentRun
        ).Value;

        AgentRun run = AgentRun.Start
        (
            kind: AgentRunKind.Research,
            chatId: thread.Id,
            assistantMessageId: assistant.Id,
            userId: thread.UserId,
            task: AgentTask.Create("Research Redis Streams").Value,
            llmModelId: LlmModelId.New(),
            startedAt: Now
        );

        _chats.Seed(thread);
        _runs.Seed(run);

        AgentRunRequested job = new(thread.Id.Value, "auth0|user-1", assistant.Id.Value, run.Id.Value);

        return (thread, assistant, run, job);
    }

    private AgentRunOrchestrator CreateOrchestrator
    (
        Func<AgentRunContext, CancellationToken, IAsyncEnumerable<TurnEvent>>? script = null,
        bool withRunner = true,
        TimeSpan? maxRunDuration = null
    )
    {
        FakeAgentRunRunner runner = new(script ?? ((_, ct) => FakeAgentRunRunner.Script([], ct)));

        return new AgentRunOrchestrator
        (
            chats: _chats,
            runs: _runs,
            runnerResolver: new FakeAgentRunnerResolver(withRunner ? runner : null),
            contextBuilder: new FakeAgentRunContextBuilder(),
            checkpointStore: new NoOpWorkflowCheckpointStore(),
            publisher: _publisher,
            stopSignal: _stopSignal,
            unitOfWork: _unitOfWork,
            dateTimeProvider: new FakeDateTimeProvider(Now),
            options: new AgentRunOptions { MaxRunDuration = maxRunDuration ?? TimeSpan.FromMinutes(45) },
            logger: NullLogger<AgentRunOrchestrator>.Instance
        );
    }

    private static AgentActivityEvent Activity(Guid turnId, int sequence, string kind = "ToolCall", string type = "web.search") =>
        new(turnId, sequence, kind, type, Title: $"Activity {sequence}", DetailJson: null);

    [Fact]
    public async Task RunAsync_HappyPath_AppendsActivitiesRecordsUsageAndCompletesWithReport()
    {
        (ChatThread thread, ChatMessage assistant, AgentRun run, AgentRunRequested job) = SeedPendingRun();
        Guid turnId = assistant.Id.Value;

        TurnEvent[] events =
        [
            Activity(turnId, 1, kind: "Phase", type: "phase"),
            Activity(turnId, 2),
            new UsageEvent(turnId, "gpt-4.1", 120, 45),
            new TokenEvent(turnId, "# Report\n\nFindings.")
        ];

        await CreateOrchestrator(script: (_, ct) => FakeAgentRunRunner.Script(events, ct))
            .RunAsync(job, CancellationToken.None);

        Assert.Equal(MessageStatus.Completed, assistant.Status);
        Assert.Equal("# Report\n\nFindings.", assistant.Content!.Value);
        Assert.Equal(2, run.Activities.Count);
        Assert.Equal(120, run.Usage.InputTokens);
        Assert.Equal(45, run.Usage.OutputTokens);
        Assert.NotNull(run.FinishedAt);
        Assert.Equal(1, _publisher.ResetCount);
        Assert.IsType<DoneEvent>(_publisher.Events[^1]);
        Assert.Equal(3, _unitOfWork.SaveCount); // 2 activity saves + 1 terminal save
    }

    [Fact]
    public async Task RunAsync_StaleSequence_SkipsWithoutFailing()
    {
        (_, ChatMessage assistant, AgentRun run, AgentRunRequested job) = SeedPendingRun();
        Guid turnId = assistant.Id.Value;

        TurnEvent[] events =
        [
            Activity(turnId, 1),
            Activity(turnId, 1),
            Activity(turnId, 2),
            new TokenEvent(turnId, "# Report")
        ];

        await CreateOrchestrator(script: (_, ct) => FakeAgentRunRunner.Script(events, ct))
            .RunAsync(job, CancellationToken.None);

        Assert.Equal(MessageStatus.Completed, assistant.Status);
        Assert.Equal(2, run.Activities.Count);
    }

    [Fact]
    public async Task RunAsync_UnparseableActivityKind_SkipsWithoutFailing()
    {
        (_, ChatMessage assistant, AgentRun run, AgentRunRequested job) = SeedPendingRun();
        Guid turnId = assistant.Id.Value;

        TurnEvent[] events =
        [
            Activity(turnId, 1, kind: "Bogus"),
            new TokenEvent(turnId, "# Report")
        ];

        await CreateOrchestrator(script: (_, ct) => FakeAgentRunRunner.Script(events, ct))
            .RunAsync(job, CancellationToken.None);

        Assert.Equal(MessageStatus.Completed, assistant.Status);
        Assert.Empty(run.Activities);
    }

    [Fact]
    public async Task RunAsync_StopRequested_HardStopsWithNullContent()
    {
        (_, ChatMessage assistant, AgentRun run, AgentRunRequested job) = SeedPendingRun();
        Guid turnId = assistant.Id.Value;

        _stopSignal.EnqueueResponse(false);
        _stopSignal.EnqueueResponse(true);

        TurnEvent[] events =
        [
            Activity(turnId, 1),
            Activity(turnId, 2),
            new TokenEvent(turnId, "# Never persisted")
        ];

        await CreateOrchestrator(script: (_, ct) => FakeAgentRunRunner.Script(events, ct))
            .RunAsync(job, CancellationToken.None);

        Assert.Equal(MessageStatus.Stopped, assistant.Status);
        Assert.Null(assistant.Content);
        Assert.NotNull(run.FinishedAt);
        Assert.IsType<StoppedEvent>(_publisher.Events[^1]);
        Assert.DoesNotContain(_publisher.Events, e => e is DoneEvent);
        Assert.Single(run.Activities);
    }

    [Fact]
    public async Task RunAsync_RunnerThrows_FailsMessageAndFinishesRun()
    {
        (_, ChatMessage assistant, AgentRun run, AgentRunRequested job) = SeedPendingRun();
        Guid turnId = assistant.Id.Value;

        await CreateOrchestrator(script: (_, ct) => FakeAgentRunRunner.EventsThenThrow
            (
                [Activity(turnId, 1)],
                new InvalidOperationException("provider down"),
                ct
            ))
            .RunAsync(job, CancellationToken.None);

        Assert.Equal(MessageStatus.Failed, assistant.Status);
        Assert.Contains("provider down", assistant.FailureReason!.Value);
        Assert.NotNull(run.FinishedAt);
        Assert.IsType<FailedEvent>(_publisher.Events[^1]);
        Assert.Single(run.Activities);
    }

    [Fact]
    public async Task RunAsync_MaxDurationExceeded_FailsTheRun()
    {
        (_, ChatMessage assistant, AgentRun run, AgentRunRequested job) = SeedPendingRun();

        await CreateOrchestrator
            (
                script: (_, ct) => FakeAgentRunRunner.Hang(ct),
                maxRunDuration: TimeSpan.FromMilliseconds(50)
            )
            .RunAsync(job, CancellationToken.None);

        Assert.Equal(MessageStatus.Failed, assistant.Status);
        Assert.Contains("maximum duration", assistant.FailureReason!.Value);
        Assert.NotNull(run.FinishedAt);
        Assert.IsType<FailedEvent>(_publisher.Events[^1]);
    }

    [Fact]
    public async Task RunAsync_WorkerShutdown_RethrowsAndLeavesMessageGenerating()
    {
        (_, ChatMessage assistant, _, AgentRunRequested job) = SeedPendingRun();

        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateOrchestrator(script: (_, ct) => FakeAgentRunRunner.Hang(ct))
                .RunAsync(job, cts.Token));

        Assert.Equal(MessageStatus.Generating, assistant.Status);
        Assert.DoesNotContain(_publisher.Events, e => e is FailedEvent);
    }

    [Fact]
    public async Task RunAsync_WhenMessageAlreadyTerminal_DoesNothing()
    {
        (ChatThread thread, ChatMessage assistant, _, AgentRunRequested job) = SeedPendingRun();
        thread.CompleteAssistantMessage(assistant.Id, MessageContent.Create("done already").Value, Now);

        await CreateOrchestrator().RunAsync(job, CancellationToken.None);

        Assert.Empty(_publisher.Events);
        Assert.Equal(0, _publisher.ResetCount);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task RunAsync_WhenRunRecordMissing_FailsTheMessage()
    {
        (ChatThread thread, ChatMessage assistant, _, AgentRunRequested job) = SeedPendingRun();
        AgentRunRequested bogusJob = job with { RunId = Guid.CreateVersion7() };

        await CreateOrchestrator().RunAsync(bogusJob, CancellationToken.None);

        Assert.Equal(MessageStatus.Failed, assistant.Status);
        Assert.IsType<FailedEvent>(_publisher.Events[^1]);
    }

    [Fact]
    public async Task RunAsync_WhenNoRunnerForKind_FailsTheMessage()
    {
        (_, ChatMessage assistant, AgentRun run, AgentRunRequested job) = SeedPendingRun();

        await CreateOrchestrator(withRunner: false).RunAsync(job, CancellationToken.None);

        Assert.Equal(MessageStatus.Failed, assistant.Status);
        Assert.NotNull(run.FinishedAt);
        Assert.IsType<FailedEvent>(_publisher.Events[^1]);
    }

    [Fact]
    public async Task RunAsync_WhenRunnerYieldsNoReport_FailsTheRun()
    {
        (_, ChatMessage assistant, AgentRun run, AgentRunRequested job) = SeedPendingRun();
        Guid turnId = assistant.Id.Value;

        await CreateOrchestrator(script: (_, ct) => FakeAgentRunRunner.Script([Activity(turnId, 1)], ct))
            .RunAsync(job, CancellationToken.None);

        Assert.Equal(MessageStatus.Failed, assistant.Status);
        Assert.NotNull(run.FinishedAt);
        Assert.IsType<FailedEvent>(_publisher.Events[^1]);
    }
}
```

- [ ] **Step 3: Run to verify compile failure**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~AgentRunOrchestratorTests"`
Expected: build error — `AgentRunOrchestrator` not found.

- [ ] **Step 4: Create `AgentRunOrchestrator.cs`**

```csharp
using System.Text;

using Chat.Application.Abstractions.AgentRuns;
using Chat.Application.Abstractions.Database;
using Chat.Application.Abstractions.Turns;
using Chat.Application.Turns;
using Chat.Domain.AgentRuns;
using Chat.Domain.AgentRuns.Entities;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Microsoft.Extensions.Logging;

using SharedKernel;

namespace Chat.Application.AgentRuns;

/// <summary>
/// Pure sequencing of one agent run, kind-agnostic (spec: orchestrators are sequencing only).
/// Everything interesting lives behind a seam; adding behavior here means a new interface,
/// never inline business logic. Mirrors ChatTurnOrchestrator's error contract.
/// </summary>
public sealed partial class AgentRunOrchestrator(
    IChatRepository chats,
    IAgentRunRepository runs,
    IAgentRunnerResolver runnerResolver,
    IAgentRunContextBuilder contextBuilder,
    IWorkflowCheckpointStore checkpointStore,
    ITokenPublisher publisher,
    ITurnStopSignal stopSignal,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    AgentRunOptions options,
    ILogger<AgentRunOrchestrator> logger
)
{
    public async Task RunAsync(AgentRunRequested job, CancellationToken cancellationToken)
    {
        ErrorOr<ChatId> chatIdResult = ChatId.Create(job.ChatId);
        ErrorOr<UserId> userIdResult = UserId.Create(job.UserId);
        ErrorOr<ChatMessageId> messageIdResult = ChatMessageId.Create(job.AssistantMessageId);
        ErrorOr<AgentRunId> runIdResult = AgentRunId.Create(job.RunId);

        if (chatIdResult.IsError || userIdResult.IsError || messageIdResult.IsError || runIdResult.IsError)
        {
            LogMalformedJob(job.ChatId, job.AssistantMessageId);
            return;
        }

        ChatThread? thread = await chats.GetByIdAsync(chatIdResult.Value, userIdResult.Value, cancellationToken);
        ChatMessage? assistantMessage = thread?.FindMessage(messageIdResult.Value);

        if (thread is null || assistantMessage is null)
        {
            LogRunTargetMissing(job.ChatId, job.AssistantMessageId);
            return;
        }

        if (assistantMessage.Status != MessageStatus.Generating)
        {
            // Redelivery after a finished run — idempotent no-op.
            LogRunAlreadyTerminal(job.AssistantMessageId);
            return;
        }

        AgentRun? run = await runs.GetByIdAsync(runIdResult.Value, cancellationToken);

        if (run is null)
        {
            LogRunRecordMissing(job.RunId, job.AssistantMessageId);
            await FailRunAsync(thread, assistantMessage, run: null, "The agent run record is missing.", cancellationToken);
            return;
        }

        IAgentRunRunner? runner = runnerResolver.Resolve(run.Kind);

        if (runner is null)
        {
            LogNoRunnerForKind(run.Kind.ToString(), job.AssistantMessageId);
            await FailRunAsync(thread, assistantMessage, run, $"No runner is registered for agent kind '{run.Kind}'.", cancellationToken);
            return;
        }

        WorkflowCheckpoint? checkpoint = await checkpointStore.GetLatestAsync(run.Id.Value, cancellationToken);

        if (checkpoint is null)
        {
            // Fresh start: a crashed previous attempt may have left a partial stream behind.
            // (PR #3: a present checkpoint resumes and appends instead.)
            await publisher.ResetAsync(job.AssistantMessageId, cancellationToken);
        }

        ErrorOr<AgentRunContext> contextResult = await contextBuilder.BuildAsync(thread, assistantMessage, run, cancellationToken);

        if (contextResult.IsError)
        {
            await FailRunAsync(thread, assistantMessage, run, contextResult.FirstError.Description, cancellationToken);
            return;
        }

        using CancellationTokenSource runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        runCts.CancelAfter(options.MaxRunDuration);

        StringBuilder reportText = new();

        try
        {
            await foreach (TurnEvent turnEvent in runner.RunAsync(contextResult.Value, checkpoint, runCts.Token))
            {
                if (await stopSignal.IsStopRequestedAsync(job.AssistantMessageId, cancellationToken))
                {
                    await StopRunAsync(thread, assistantMessage, run, cancellationToken);
                    return;
                }

                switch (turnEvent)
                {
                    case TokenEvent token:
                        reportText.Append(token.Text);
                        break;

                    case AgentActivityEvent activity:
                        await AppendActivityAsync(run, activity, cancellationToken);
                        break;

                    case UsageEvent usage:
                        RecordUsage(run, usage);
                        break;
                }

                await publisher.PublishAsync(turnEvent, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (runCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // The max-duration timer fired, not the host: terminal failure — a resumed
            // redelivery would only time out again and again.
            await FailRunAsync
            (
                thread,
                assistantMessage,
                run,
                $"The run exceeded the maximum duration of {options.MaxRunDuration.TotalMinutes:F0} minutes.",
                CancellationToken.None
            );
            return;
        }
        catch (OperationCanceledException)
        {
            // Worker shutdown mid-run: leave the message Generating; redelivery restarts from
            // scratch (spec decision 2) and stale-sequence skips make replays harmless.
            throw;
        }
#pragma warning disable CA1031 // Last-chance boundary: agent exceptions are unenumerable; an uncaught type leaves the card Generating forever.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogAgentRunFailed(exception, job.AssistantMessageId);
            await FailRunAsync(thread, assistantMessage, run, exception.Message, cancellationToken);
            return;
        }

        string report = reportText.ToString();

        if (report.Length > MessageContent.MaxLength)
        {
            LogReportTruncated(job.AssistantMessageId, report.Length);
            report = report[..MessageContent.MaxLength];
        }

        ErrorOr<MessageContent> contentResult = MessageContent.Create(report);

        if (contentResult.IsError)
        {
            await FailRunAsync(thread, assistantMessage, run, "The agent returned an empty report.", cancellationToken);
            return;
        }

        ErrorOr<ChatMessage> completionResult = thread.CompleteAssistantMessage
        (
            messageId: assistantMessage.Id,
            content: contentResult.Value,
            completedAt: dateTimeProvider.UtcNow
        );

        if (completionResult.IsError)
        {
            LogRunAlreadyTerminal(job.AssistantMessageId);
            return;
        }

        FinishRun(run);
        await checkpointStore.DeleteAllAsync(run.Id.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await publisher.PublishAsync(new DoneEvent(job.AssistantMessageId), cancellationToken);
    }

    private async Task AppendActivityAsync(AgentRun run, AgentActivityEvent activity, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse(activity.Kind, ignoreCase: false, out ActivityKind kind))
        {
            LogInvalidActivitySkipped(run.Id.Value, activity.Sequence, activity.Kind);
            return;
        }

        ErrorOr<ActivitySequence> sequence = ActivitySequence.Create(activity.Sequence);
        ErrorOr<ActivityType> type = ActivityType.Create(activity.Type);
        ErrorOr<ActivityTitle> title = ActivityTitle.Create(activity.Title);
        ErrorOr<ActivityDetail>? detail = activity.DetailJson is null ? null : ActivityDetail.Create(activity.DetailJson);

        if (sequence.IsError || type.IsError || title.IsError || detail is { IsError: true })
        {
            LogInvalidActivitySkipped(run.Id.Value, activity.Sequence, activity.Kind);
            return;
        }

        ErrorOr<AgentRunActivity> appended = run.AppendActivity
        (
            sequence: sequence.Value,
            kind: kind,
            type: type.Value,
            title: title.Value,
            detail: detail?.Value,
            occurredAt: dateTimeProvider.UtcNow
        );

        if (appended.IsError)
        {
            // Stale sequence = replay after a restart — skip by contract. Anything else is a
            // runner bug worth a warning, but never worth killing the run over one bad row.
            LogActivitySkipped(run.Id.Value, activity.Sequence, appended.FirstError.Code);
            return;
        }

        // Durable progress advances with the run: a crash never loses observed activities.
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private void RecordUsage(AgentRun run, UsageEvent usage)
    {
        ErrorOr<TokenUsage> delta = TokenUsage.Create(usage.InputTokens, usage.OutputTokens);

        if (delta.IsError)
        {
            return;
        }

        _ = run.RecordUsage(delta.Value); // Persisted with the next activity or terminal save.
    }

    private void FinishRun(AgentRun run)
    {
        ErrorOr<Success> finished = run.Finish(dateTimeProvider.UtcNow);

        if (finished.IsError)
        {
            LogRunFinishRejected(run.Id.Value, finished.FirstError.Code);
        }
    }

    private async Task StopRunAsync(ChatThread thread, ChatMessage assistantMessage, AgentRun run, CancellationToken cancellationToken)
    {
        ErrorOr<ChatMessage> stopResult = thread.StopAssistantMessage
        (
            messageId: assistantMessage.Id,
            content: null, // Hard stop (spec decision 5): no report, activities remain.
            stoppedAt: dateTimeProvider.UtcNow
        );

        if (stopResult.IsError)
        {
            LogRunAlreadyTerminal(assistantMessage.Id.Value);
            return;
        }

        FinishRun(run);
        await checkpointStore.DeleteAllAsync(run.Id.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await publisher.PublishAsync(new StoppedEvent(assistantMessage.Id.Value), cancellationToken);
    }

    private async Task FailRunAsync
    (
        ChatThread thread,
        ChatMessage assistantMessage,
        AgentRun? run,
        string reason,
        CancellationToken cancellationToken
    )
    {
        string truncated = reason.Length <= FailureReason.MaxLength ? reason : reason[..FailureReason.MaxLength];

        ErrorOr<FailureReason> failureReason = FailureReason.Create(truncated);

        ErrorOr<ChatMessage> failure = thread.FailAssistantMessage
        (
            messageId: assistantMessage.Id,
            reason: failureReason.IsError ? FailureReason.Create("The agent run failed.").Value : failureReason.Value,
            failedAt: dateTimeProvider.UtcNow
        );

        if (failure.IsError)
        {
            LogRunAlreadyTerminal(assistantMessage.Id.Value);
            return;
        }

        if (run is not null)
        {
            FinishRun(run);
            await checkpointStore.DeleteAllAsync(run.Id.Value, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await publisher.PublishAsync(new FailedEvent(assistantMessage.Id.Value, truncated), cancellationToken);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Discarded malformed agent run job for chat {ChatId}, message {AssistantMessageId}")]
    private partial void LogMalformedJob(Guid chatId, Guid assistantMessageId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Agent run target not found for chat {ChatId}, message {AssistantMessageId}")]
    private partial void LogRunTargetMissing(Guid chatId, Guid assistantMessageId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information,
        Message = "Agent run for message {AssistantMessageId} is already terminal; skipping (idempotent redelivery)")]
    private partial void LogRunAlreadyTerminal(Guid assistantMessageId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error,
        Message = "Agent run failed for message {AssistantMessageId}")]
    private partial void LogAgentRunFailed(Exception exception, Guid assistantMessageId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error,
        Message = "Agent run record {RunId} missing for message {AssistantMessageId}")]
    private partial void LogRunRecordMissing(Guid runId, Guid assistantMessageId);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error,
        Message = "No runner registered for agent kind {Kind}; failing message {AssistantMessageId}")]
    private partial void LogNoRunnerForKind(string kind, Guid assistantMessageId);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning,
        Message = "Skipped unparseable activity (run {RunId}, sequence {Sequence}, kind {Kind})")]
    private partial void LogInvalidActivitySkipped(Guid runId, int sequence, string kind);

    [LoggerMessage(EventId = 8, Level = LogLevel.Information,
        Message = "Skipped activity append (run {RunId}, sequence {Sequence}): {ErrorCode}")]
    private partial void LogActivitySkipped(Guid runId, int sequence, string errorCode);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning,
        Message = "Run {RunId} finish rejected: {ErrorCode}")]
    private partial void LogRunFinishRejected(Guid runId, string errorCode);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning,
        Message = "Report for message {AssistantMessageId} truncated from {Length} characters")]
    private partial void LogReportTruncated(Guid assistantMessageId, int length);
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~AgentRunOrchestratorTests"`
Expected: PASS (11 tests).

- [ ] **Step 6: Run the full application suite** (guards against regressions in shared fakes)

Run: `dotnet test tests/Chat/Chat.Application.Tests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/services/Chat/Chat.Application tests/Chat/Chat.Application.Tests
git commit -m "feat(agents): add kind-agnostic agent run orchestrator"
```

---

## Task 9: Infrastructure — resolver, consumer, dedicated queue, worker DI

No unit tests (infrastructure; project rule) — verified by build and Task 15.

**Files:**
- Create: `src/services/Chat/Chat.Infrastructure/AgentRuns/AgentRunnerResolver.cs`
- Create: `src/services/Chat/Chat.Infrastructure/AgentRuns/Consumers/AgentRunRequestedConsumer.cs`
- Create: `src/services/Chat/Chat.Infrastructure/AgentRuns/Consumers/AgentRunRequestedConsumerDefinition.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`
- Modify: `src/services/Chat/Chat.TurnWorker/appsettings.json`

**Interfaces:**
- Consumes: `AgentRunOrchestrator.RunAsync` (Task 8); `AgentRunOptions` (Task 5); `IAgentRunnerResolver`, `IWorkflowCheckpointStore`, `IAgentRunContextBuilder` seams.
- Produces: dedicated `agent-run-requested` queue (kebab-case formatter names it from the consumer); keyed-DI resolver — Task 11 registers the research runner under `AgentRunKind.Research`.

- [ ] **Step 1: Create `AgentRunnerResolver.cs`**

```csharp
using Chat.Application.Abstractions.AgentRuns;
using Chat.Domain.AgentRuns.ValueObjects;

using Microsoft.Extensions.DependencyInjection;

namespace Chat.Infrastructure.AgentRuns;

/// <summary>Keyed-DI lookup: a new agent kind = one AddKeyedScoped registration, nothing else.</summary>
internal sealed class AgentRunnerResolver(IServiceProvider serviceProvider) : IAgentRunnerResolver
{
    public IAgentRunRunner? Resolve(AgentRunKind kind) =>
        serviceProvider.GetKeyedService<IAgentRunRunner>(kind);
}
```

- [ ] **Step 2: Create the consumer** — ack/retry semantics only; all logic lives in the orchestrator:

```csharp
using Chat.Application.AgentRuns;

using MassTransit;

namespace Chat.Infrastructure.AgentRuns.Consumers;

internal sealed class AgentRunRequestedConsumer(AgentRunOrchestrator orchestrator) : IConsumer<AgentRunRequested>
{
    public async Task Consume(ConsumeContext<AgentRunRequested> context) =>
        await orchestrator.RunAsync(context.Message, context.CancellationToken);
}
```

- [ ] **Step 3: Create the consumer definition** — dedicated low-concurrency queue so long research runs never starve the 4-slot chat-turn consumer; broker ack window set explicitly above `MaxRunDuration`:

```csharp
using Chat.Application.AgentRuns;

using MassTransit;

using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.AgentRuns.Consumers;

internal sealed class AgentRunRequestedConsumerDefinition : ConsumerDefinition<AgentRunRequestedConsumer>
{
    private static readonly TimeSpan ConsumerAckTimeout = TimeSpan.FromMinutes(60);

    public AgentRunRequestedConsumerDefinition(IOptions<AgentRunOptions> options)
    {
        // In-flight agent runs per worker replica. Deliberately low: runs are long and expensive.
        ConcurrentMessageLimit = options.Value.QueueConcurrency;
    }

    protected override void ConfigureConsumer
    (
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<AgentRunRequestedConsumer> consumerConfigurator,
        IRegistrationContext context
    )
    {
        // Retries cover only exceptions thrown before the run streams (transient load
        // failures); the orchestrator acks semantic failures terminally by design.
        endpointConfigurator.UseMessageRetry(retry =>
            retry.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));

        // RabbitMQ's per-queue delivery ack timeout must exceed MaxRunDuration (45 min default),
        // or the broker force-closes the channel mid-run.
        if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rabbitMq)
        {
            rabbitMq.SetQueueArgument("x-consumer-timeout", (long)ConsumerAckTimeout.TotalMilliseconds);
        }
    }
}
```

- [ ] **Step 4: Wire DI in `DependencyInjection.cs`**

1. Add usings:

```csharp
using Chat.Application.Abstractions.AgentRuns;
using Chat.Application.AgentRuns;
using Chat.Infrastructure.AgentRuns;
using Chat.Infrastructure.AgentRuns.Consumers;
```

2. Extend the worker composition root — add one line to `AddTurnWorkerInfrastructure`'s chain, after `.AddTurnPipeline(configuration)`:

```csharp
            .AddAgentRunPipeline(configuration)
```

3. Append the new private method inside the class:

```csharp
    private static IServiceCollection AddAgentRunPipeline(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<AgentRunOptions>()
            .Bind(configuration.GetSection(AgentRunOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The orchestrator takes the options value directly (Application stays package-free).
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<AgentRunOptions>>().Value);

        services.AddScoped<AgentRunOrchestrator>();
        services.AddScoped<IAgentRunContextBuilder, AgentRunContextBuilder>();
        services.AddScoped<IAgentRunnerResolver, AgentRunnerResolver>();

        // PR #3 replaces this single registration with the Postgres store + resume path.
        services.AddSingleton<IWorkflowCheckpointStore, NoOpWorkflowCheckpointStore>();

        return services;
    }
```

4. Register the consumer in `AddTurnWorkerMessaging`, next to the existing turn consumer:

```csharp
            configurator.AddConsumer<AgentRunRequestedConsumer, AgentRunRequestedConsumerDefinition>();
```

> `AgentRunContextBuilder` is `internal` in Chat.Application — like `ContextBuilder`, it is registered from Chat.Infrastructure. If the compiler rejects the reference, check how `ContextBuilder` is exposed (the existing `AddTurnPipeline` registers it the same way) and mirror that mechanism exactly.

- [ ] **Step 5: Add worker configuration** — in `src/services/Chat/Chat.TurnWorker/appsettings.json`, add a top-level section (alongside the existing ones):

```json
  "AgentRuns": {
    "MaxRunDuration": "00:45:00",
    "QueueConcurrency": 1
  }
```

- [ ] **Step 6: Build**

Run: `dotnet build Nova.slnx`
Expected: PASS. (No runner is registered yet — the resolver returns null and the orchestrator fails such runs gracefully; Task 11 adds the research registration.)

- [ ] **Step 7: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure src/services/Chat/Chat.TurnWorker
git commit -m "feat(agents): add agent run consumer with dedicated queue and worker wiring"
```

---

## Task 10: Infrastructure — MAF Workflows package, options, and research executors

Everything in this task and Task 11 lives under `src/services/Chat/Chat.Infrastructure/Agents/Research/` (quarantine rule). The workflow is a deliberately **linear** graph (Planner → Search → Read → Critic → loop-or-Writer): supersteps run one executor at a time, so the flowing `ResearchState` message carries the activity sequence counter — sequences stay deterministic, which is what makes orchestrator-side dedup (and PR #3's resume) work with zero shared-state machinery. Executors are stateless; all state travels in `ResearchState`.

No unit tests (MAF-coupled infrastructure; project rule) — verification is compilation plus Task 15.

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj`
- Create: `src/services/Chat/Chat.Infrastructure/Options/ResearchOptions.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Agents/Research/ResearchActivityTypes.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Agents/Research/ResearchState.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Agents/Research/ResearchProgressEvents.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Agents/Research/ResearchPrompts.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Agents/Research/Executors/PlannerExecutor.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Agents/Research/Executors/SearchExecutor.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Agents/Research/Executors/ReadExecutor.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Agents/Research/Executors/CriticExecutor.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Agents/Research/Executors/WriterExecutor.cs`

**Interfaces:**
- Consumes: `IWebSearchClient.SearchAsync(query, count, ct)` → `WebSearchResult(Title, ReferencedSite, Snippet, PublishedAt)` — **`ReferencedSite` already carries the full result URL** (`ExaWebSearchClient` maps `result.Url` into it; verified); `IUrlReader.ReadAsync(uri, ct)` → `ReadPage(Url, Title, Markdown)`; `ActivityKind` enum names.
- Produces (used verbatim by Task 11): `ResearchState`, `ResearchFinding`, `ResearchProgress(int Sequence, string Kind, string Type, string Title, string? DetailJson)`, `ResearchProgressEvent`/`ResearchUsageEvent` (MAF `WorkflowEvent` subclasses), the five executors, `ResearchOptions`, `ResearchActivityTypes`.

- [ ] **Step 1: Pin the MAF Workflows API surface (verification step — do this FIRST)**

This task writes against the documented 1.13-era API. Add the package (Step 2), then confirm each symbol with Go-to-Definition; where the installed package differs, align the code in Steps 4–8 and Task 11 **without changing any seam or file boundary** (MAF types must never leave `Chat.Infrastructure/Agents/`):

1. `Executor` base class + `[MessageHandler]` attribute; handler shape `private async ValueTask HandleAsync(ResearchState state, IWorkflowContext context)`.
2. `IWorkflowContext.SendMessageAsync(object)`, `IWorkflowContext.YieldOutputAsync(object)`, `IWorkflowContext.AddEventAsync(WorkflowEvent)`.
3. `WorkflowBuilder(startExecutor)`, `.AddEdge(from, to)`, conditional overload `.AddEdge<ResearchState>(from, to, condition: ...)`, `.WithOutputFrom(writer)`, `.Build()`.
4. `InProcessExecution.RunStreamingAsync(workflow, input, ct)` → `StreamingRun`, `run.WatchStreamAsync(ct)`, `WorkflowOutputEvent.Data`, and the workflow-error event type surfaced by `WatchStreamAsync`.
5. `AgentRunResponse.Usage` member names (`Microsoft.Extensions.AI.UsageDetails.InputTokenCount/OutputTokenCount`) and `chatClient.AsAIAgent(instructions:)` (the pattern `AgentFrameworkRunner` already uses).

Reference: the MAF workflows docs (learn.microsoft.com/en-us/agent-framework/workflows). Checkpoint APIs are deliberately NOT used in this PR.

- [ ] **Step 2: Add the package**

In `Directory.Packages.props`, next to the existing MAF entries:

```xml
    <PackageVersion Include="Microsoft.Agents.AI.Workflows" Version="1.13.0" />
```

In `src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj`, next to the existing MAF references:

```xml
    <PackageReference Include="Microsoft.Agents.AI.Workflows" />
```

Run `dotnet restore Nova.slnx` — if 1.13.0 of the Workflows package does not exist on NuGet, pin the closest version matching the installed `Microsoft.Agents.AI` line (check with `dotnet list src/services/Chat/Chat.Infrastructure package | grep Agents`).

- [ ] **Step 3: Create `ResearchOptions.cs`** (workflow budgets only — run duration and queue concurrency are the generic `AgentRunOptions`' concern):

```csharp
using System.ComponentModel.DataAnnotations;

namespace Chat.Infrastructure.Options;

public sealed class ResearchOptions
{
    public const string SectionName = "Research";

    [Range(1, 10)]
    public int MaxRounds { get; init; } = 3;

    [Range(1, 50)]
    public int MaxSearches { get; init; } = 12;

    [Range(1, 40)]
    public int MaxSourcesToRead { get; init; } = 10;
}
```

- [ ] **Step 4: Create `ResearchActivityTypes.cs`** — the research-owned `ActivityType` vocabulary (spec §8 table). Lowercase `[a-z0-9._-]` per the `ActivityType` VO rules:

```csharp
namespace Chat.Infrastructure.Agents.Research;

/// <summary>
/// Research's ActivityType vocabulary (spec §8). Kind-owned: other agent kinds define their own
/// strings; the generic pipeline never interprets these beyond displaying and counting them.
/// </summary>
internal static class ResearchActivityTypes
{
    public const string Phase = "phase";
    public const string Search = "web.search";
    public const string Read = "web.read";
    public const string Source = "source";
    public const string Reasoning = "reasoning";
    public const string ReadFailed = "read.failed";
}
```

- [ ] **Step 5: Create the state and progress event types**

`ResearchState.cs` (must stay JSON-serializable — it is the message MAF checkpoints in PR #3):

```csharp
namespace Chat.Infrastructure.Agents.Research;

internal sealed record ResearchFinding(string Url, string Title, string Notes);

/// <summary>
/// The single message flowing through the linear research graph. Carries the activity sequence
/// counter so sequences are deterministic (the orchestrator dedups by sequence, and PR #3's
/// checkpoint resume re-emits the same numbers for replayed supersteps).
/// </summary>
internal sealed record ResearchState
(
    string Brief,
    IReadOnlyList<string> History,
    IReadOnlyList<string> OpenQuestions,
    IReadOnlyList<string> CandidateUrls,
    IReadOnlyList<ResearchFinding> Findings,
    int Round,
    int SearchesUsed,
    int SourcesRead,
    int NextSequence
)
{
    public static ResearchState Start(string brief, IReadOnlyList<string> history) => new
    (
        Brief: brief,
        History: history,
        OpenQuestions: [],
        CandidateUrls: [],
        Findings: [],
        Round: 0,
        SearchesUsed: 0,
        SourcesRead: 0,
        NextSequence: 1
    );
}
```

`ResearchProgressEvents.cs`:

```csharp
using Microsoft.Agents.AI.Workflows;

namespace Chat.Infrastructure.Agents.Research;

/// <summary>Kind = ActivityKind member name ("Phase"/"Thought"/...); Type = ResearchActivityTypes value.</summary>
internal sealed record ResearchProgress(int Sequence, string Kind, string Type, string Title, string? DetailJson);

internal sealed class ResearchProgressEvent(ResearchProgress progress) : WorkflowEvent
{
    public ResearchProgress Progress { get; } = progress;
}

internal sealed class ResearchUsageEvent(int inputTokens, int outputTokens) : WorkflowEvent
{
    public int InputTokens { get; } = inputTokens;

    public int OutputTokens { get; } = outputTokens;
}
```

(If `WorkflowEvent` requires a constructor argument in the installed package, pass the payload through it and keep the typed properties.)

- [ ] **Step 6: Create `ResearchPrompts.cs`**

```csharp
using System.Text;

namespace Chat.Infrastructure.Agents.Research;

internal static class ResearchPrompts
{
    public const string PlannerInstructions =
        "You are a research planner. Given a research brief, produce focused web search queries " +
        "that together cover the question. Output ONLY the queries, one per line, no numbering.";

    public const string CondenserInstructions =
        "You extract facts. Given a research brief and a page, list only the concrete facts, figures, " +
        "dates, and claims relevant to the brief, as short bullet lines. If nothing is relevant, output NOTHING.";

    public const string CriticInstructions =
        "You are a research critic. Given a brief and collected findings, decide whether the findings " +
        "suffice to answer the brief. If they suffice, output exactly DONE. Otherwise output ONLY new web " +
        "search queries that close the remaining gaps, one per line, no numbering.";

    public const string WriterInstructions =
        "You are a research writer. Write a well-structured markdown report answering the brief using ONLY " +
        "the provided findings. Cite sources inline as [n] matching the numbered source list, and end with " +
        "a '## Sources' section listing each source as '[n] Title — URL'. Be factual; note open questions.";

    public static string Planner(string brief, IReadOnlyList<string> history)
    {
        StringBuilder prompt = new();

        if (history.Count > 0)
        {
            prompt.AppendLine("Conversation context:");

            foreach (string line in history)
            {
                prompt.AppendLine(line);
            }

            prompt.AppendLine();
        }

        prompt.AppendLine("Research brief:");
        prompt.AppendLine(brief);

        return prompt.ToString();
    }

    public static string Condense(string brief, string url, string? title, string markdown)
    {
        const int maxPageChars = 6000;

        string page = markdown.Length <= maxPageChars ? markdown : markdown[..maxPageChars];

        return $"Research brief:\n{brief}\n\nPage: {title} ({url})\n\n{page}";
    }

    public static string Critic(string brief, IReadOnlyList<ResearchFinding> findings)
    {
        StringBuilder prompt = new();
        prompt.AppendLine($"Research brief:\n{brief}\n");
        prompt.AppendLine("Findings so far:");

        foreach (ResearchFinding finding in findings)
        {
            prompt.AppendLine($"- {finding.Title} ({finding.Url}): {finding.Notes}");
        }

        return prompt.ToString();
    }

    public static string Writer(string brief, IReadOnlyList<ResearchFinding> findings)
    {
        StringBuilder prompt = new();
        prompt.AppendLine($"Research brief:\n{brief}\n");
        prompt.AppendLine("Numbered sources and findings:");

        for (int i = 0; i < findings.Count; i++)
        {
            ResearchFinding finding = findings[i];
            prompt.AppendLine($"[{i + 1}] {finding.Title} — {finding.Url}");
            prompt.AppendLine(finding.Notes);
        }

        return prompt.ToString();
    }

    public static IReadOnlyList<string> ParseQueries(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("DONE", StringComparison.OrdinalIgnoreCase))
            .Select(line => line.TrimStart('-', '*', ' ').Trim())
            .Where(line => line.Length > 2)
            .Take(5)
            .ToList();
}
```

- [ ] **Step 7: Create the executors**

Shared emission idiom: sequences come off `state.NextSequence`; every emit increments a local counter, and the forwarded state carries the advanced value. `Kind` strings use `nameof(ActivityKind.X)` so a domain rename breaks the build instead of silently desyncing.

`Executors/PlannerExecutor.cs`:

```csharp
using System.Text.Json;

using Chat.Domain.AgentRuns.ValueObjects;

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace Chat.Infrastructure.Agents.Research.Executors;

internal sealed class PlannerExecutor(AIAgent agent) : Executor("research-planner")
{
    [MessageHandler]
    private async ValueTask HandleAsync(ResearchState state, IWorkflowContext context)
    {
        int sequence = state.NextSequence;

        await context.AddEventAsync(new ResearchProgressEvent(new ResearchProgress
        (
            Sequence: sequence++,
            Kind: nameof(ActivityKind.Phase),
            Type: ResearchActivityTypes.Phase,
            Title: "Planning",
            DetailJson: null
        )));

        AgentRunResponse response = await agent.RunAsync(ResearchPrompts.Planner(state.Brief, state.History));

        await EmitUsageAsync(context, response);

        IReadOnlyList<string> questions = ResearchPrompts.ParseQueries(response.Text);

        if (questions.Count == 0)
        {
            questions = [state.Brief];
        }

        await context.AddEventAsync(new ResearchProgressEvent(new ResearchProgress
        (
            Sequence: sequence++,
            Kind: nameof(ActivityKind.Thought),
            Type: ResearchActivityTypes.Reasoning,
            Title: $"Planned {questions.Count} research questions",
            DetailJson: JsonSerializer.Serialize(new { questions })
        )));

        await context.SendMessageAsync(state with
        {
            OpenQuestions = questions,
            NextSequence = sequence
        });
    }

    internal static async ValueTask EmitUsageAsync(IWorkflowContext context, AgentRunResponse response)
    {
        if (response.Usage is { } usage)
        {
            await context.AddEventAsync(new ResearchUsageEvent
            (
                inputTokens: (int)(usage.InputTokenCount ?? 0),
                outputTokens: (int)(usage.OutputTokenCount ?? 0)
            ));
        }
    }
}
```

`Executors/SearchExecutor.cs`:

```csharp
using System.Text.Json;

using Chat.Application.Abstractions.WebSearch;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Infrastructure.Options;

using Microsoft.Agents.AI.Workflows;

namespace Chat.Infrastructure.Agents.Research.Executors;

internal sealed class SearchExecutor(IWebSearchClient searchClient, ResearchOptions options) : Executor("research-search")
{
    private const int ResultsPerQuery = 5;
    private const int QueriesPerRound = 3;

    [MessageHandler]
    private async ValueTask HandleAsync(ResearchState state, IWorkflowContext context)
    {
        int sequence = state.NextSequence;

        await context.AddEventAsync(new ResearchProgressEvent(new ResearchProgress
        (
            Sequence: sequence++,
            Kind: nameof(ActivityKind.Phase),
            Type: ResearchActivityTypes.Phase,
            Title: "Searching the web",
            DetailJson: null
        )));

        List<string> candidates = [.. state.CandidateUrls];
        int searchesUsed = state.SearchesUsed;

        foreach (string question in state.OpenQuestions.Take(QueriesPerRound))
        {
            if (searchesUsed >= options.MaxSearches)
            {
                break;
            }

            await context.AddEventAsync(new ResearchProgressEvent(new ResearchProgress
            (
                Sequence: sequence++,
                Kind: nameof(ActivityKind.ToolCall),
                Type: ResearchActivityTypes.Search,
                Title: $"Searching: {question}",
                DetailJson: JsonSerializer.Serialize(new { query = question })
            )));

            searchesUsed++;

            IReadOnlyList<WebSearchResult> results =
                await searchClient.SearchAsync(question, ResultsPerQuery, CancellationToken.None);

            foreach (WebSearchResult result in results)
            {
                string url = result.ReferencedSite;

                if (!candidates.Contains(url) &&
                    !state.Findings.Any(finding => finding.Url == url) &&
                    Uri.IsWellFormedUriString(url, UriKind.Absolute))
                {
                    candidates.Add(url);
                }
            }
        }

        await context.SendMessageAsync(state with
        {
            CandidateUrls = candidates,
            SearchesUsed = searchesUsed,
            NextSequence = sequence
        });
    }
}
```

(On cancellation tokens in executors: if the installed `[MessageHandler]` signature offers a `CancellationToken`, accept it and pass it through instead of `CancellationToken.None`; the orchestrator's linked token already bounds the whole workflow either way.)

`Executors/ReadExecutor.cs`:

```csharp
using System.Text.Json;

using Chat.Application.Abstractions.WebRead;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Infrastructure.Options;

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace Chat.Infrastructure.Agents.Research.Executors;

internal sealed class ReadExecutor(IUrlReader urlReader, AIAgent condenser, ResearchOptions options) : Executor("research-read")
{
    private const int ReadsPerRound = 3;

    [MessageHandler]
    private async ValueTask HandleAsync(ResearchState state, IWorkflowContext context)
    {
        int sequence = state.NextSequence;
        List<ResearchFinding> findings = [.. state.Findings];
        List<string> remaining = [.. state.CandidateUrls];
        int sourcesRead = state.SourcesRead;
        int readsThisRound = 0;

        while (remaining.Count > 0 && readsThisRound < ReadsPerRound && sourcesRead < options.MaxSourcesToRead)
        {
            string url = remaining[0];
            remaining.RemoveAt(0);
            readsThisRound++;

            string host = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? uri.Host : url;

            await context.AddEventAsync(new ResearchProgressEvent(new ResearchProgress
            (
                Sequence: sequence++,
                Kind: nameof(ActivityKind.ToolCall),
                Type: ResearchActivityTypes.Read,
                Title: $"Reading {host}",
                DetailJson: JsonSerializer.Serialize(new { url })
            )));

            if (uri is null)
            {
                continue;
            }

            ReadPage page;

            try
            {
                page = await urlReader.ReadAsync(uri, CancellationToken.None);
            }
#pragma warning disable CA1031 // A single unreadable page must not kill the run.
            catch (Exception)
#pragma warning restore CA1031
            {
                await context.AddEventAsync(new ResearchProgressEvent(new ResearchProgress
                (
                    Sequence: sequence++,
                    Kind: nameof(ActivityKind.Error),
                    Type: ResearchActivityTypes.ReadFailed,
                    Title: $"Could not read {host}; skipping",
                    DetailJson: JsonSerializer.Serialize(new { url })
                )));
                continue;
            }

            AgentRunResponse response = await condenser.RunAsync(
                ResearchPrompts.Condense(state.Brief, url, page.Title, page.Markdown));

            await PlannerExecutor.EmitUsageAsync(context, response);

            if (string.IsNullOrWhiteSpace(response.Text))
            {
                continue;
            }

            sourcesRead++;
            findings.Add(new ResearchFinding(Url: url, Title: page.Title ?? host, Notes: response.Text.Trim()));

            await context.AddEventAsync(new ResearchProgressEvent(new ResearchProgress
            (
                Sequence: sequence++,
                Kind: nameof(ActivityKind.Observation),
                Type: ResearchActivityTypes.Source,
                Title: page.Title ?? host,
                DetailJson: JsonSerializer.Serialize(new { url, host })
            )));
        }

        await context.SendMessageAsync(state with
        {
            CandidateUrls = remaining,
            Findings = findings,
            SourcesRead = sourcesRead,
            NextSequence = sequence
        });
    }
}
```

`Executors/CriticExecutor.cs`:

```csharp
using System.Text.Json;

using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Infrastructure.Options;

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace Chat.Infrastructure.Agents.Research.Executors;

/// <summary>
/// Decides one more round or done. "Done" = OpenQuestions comes out empty — the workflow
/// edges route on that (empty → Writer, non-empty → Search).
/// </summary>
internal sealed class CriticExecutor(AIAgent agent, ResearchOptions options) : Executor("research-critic")
{
    [MessageHandler]
    private async ValueTask HandleAsync(ResearchState state, IWorkflowContext context)
    {
        int sequence = state.NextSequence;
        int round = state.Round + 1;

        await context.AddEventAsync(new ResearchProgressEvent(new ResearchProgress
        (
            Sequence: sequence++,
            Kind: nameof(ActivityKind.Phase),
            Type: ResearchActivityTypes.Phase,
            Title: "Analyzing findings",
            DetailJson: null
        )));

        IReadOnlyList<string> nextQuestions = [];

        bool budgetExhausted = round >= options.MaxRounds
            || state.SearchesUsed >= options.MaxSearches
            || state.SourcesRead >= options.MaxSourcesToRead
            || state.Findings.Count == 0;

        if (!budgetExhausted)
        {
            AgentRunResponse response = await agent.RunAsync(ResearchPrompts.Critic(state.Brief, state.Findings));

            await PlannerExecutor.EmitUsageAsync(context, response);

            if (!response.Text.Trim().StartsWith("DONE", StringComparison.OrdinalIgnoreCase))
            {
                nextQuestions = ResearchPrompts.ParseQueries(response.Text);
            }
        }

        await context.AddEventAsync(new ResearchProgressEvent(new ResearchProgress
        (
            Sequence: sequence++,
            Kind: nameof(ActivityKind.Thought),
            Type: ResearchActivityTypes.Reasoning,
            Title: nextQuestions.Count == 0
                ? "Coverage sufficient; moving to the report"
                : $"Identified {nextQuestions.Count} gaps; searching again",
            DetailJson: JsonSerializer.Serialize(new { round, gaps = nextQuestions })
        )));

        await context.SendMessageAsync(state with
        {
            OpenQuestions = nextQuestions,
            Round = round,
            NextSequence = sequence
        });
    }
}
```

`Executors/WriterExecutor.cs`:

```csharp
using Chat.Domain.AgentRuns.ValueObjects;

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace Chat.Infrastructure.Agents.Research.Executors;

internal sealed class WriterExecutor(AIAgent agent) : Executor("research-writer")
{
    [MessageHandler]
    private async ValueTask HandleAsync(ResearchState state, IWorkflowContext context)
    {
        await context.AddEventAsync(new ResearchProgressEvent(new ResearchProgress
        (
            Sequence: state.NextSequence,
            Kind: nameof(ActivityKind.Phase),
            Type: ResearchActivityTypes.Phase,
            Title: "Writing the report",
            DetailJson: null
        )));

        AgentRunResponse response = await agent.RunAsync(ResearchPrompts.Writer(state.Brief, state.Findings));

        await PlannerExecutor.EmitUsageAsync(context, response);

        await context.YieldOutputAsync(response.Text);
    }
}
```

- [ ] **Step 8: Build**

Run: `dotnet build Nova.slnx`
Expected: PASS. Fix any MAF surface drift per Step 1's rules (structure and seams stay identical).

- [ ] **Step 9: Commit**

```bash
git add Directory.Packages.props src/services/Chat/Chat.Infrastructure
git commit -m "feat(agents): add research workflow executors on agent framework workflows"
```

---

## Task 11: Infrastructure — `ResearchWorkflowRunner` + keyed registration

The only `IAgentRunRunner` implementation: builds the graph per run (agents bound to the user-selected model), streams workflow events, and maps them to `TurnEvent`s. The `checkpoint` parameter is deliberately ignored in this PR (always null from the no-op store); PR #3 adds the `CheckpointManager` + resume path here without touching any seam.

**Files:**
- Create: `src/services/Chat/Chat.Infrastructure/Agents/Research/ResearchWorkflowRunner.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`
- Modify: `src/services/Chat/Chat.TurnWorker/appsettings.json`

**Interfaces:**
- Consumes: everything from Tasks 5 and 10; `AgentOptions` (existing, `Chat.Infrastructure.Options`); `AgentActivityEvent`/`UsageEvent`/`TokenEvent`.
- Produces: `ResearchWorkflowRunner : IAgentRunRunner`, registered keyed under `AgentRunKind.Research`.

- [ ] **Step 1: Create `ResearchWorkflowRunner.cs`**

```csharp
using System.ClientModel;
using System.Runtime.CompilerServices;

using Chat.Application.Abstractions.AgentRuns;
using Chat.Application.Abstractions.Turns;
using Chat.Application.Abstractions.WebRead;
using Chat.Application.Abstractions.WebSearch;
using Chat.Application.Turns;
using Chat.Infrastructure.Agents.Research.Executors;
using Chat.Infrastructure.Options;

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Options;

using OpenAI;

namespace Chat.Infrastructure.Agents.Research;

internal sealed class ResearchWorkflowRunner : IAgentRunRunner
{
    private readonly OpenAIClient _client;
    private readonly ResearchOptions _research;
    private readonly IWebSearchClient _searchClient;
    private readonly IUrlReader _urlReader;

    public ResearchWorkflowRunner
    (
        IOptions<AgentOptions> agentOptions,
        IOptions<ResearchOptions> researchOptions,
        IWebSearchClient searchClient,
        IUrlReader urlReader
    )
    {
        AgentOptions agent = agentOptions.Value;

        _client = new OpenAIClient
        (
            new ApiKeyCredential(agent.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(agent.BaseUrl.ToString()) }
        );

        _research = researchOptions.Value;
        _searchClient = searchClient;
        _urlReader = urlReader;
    }

    public async IAsyncEnumerable<TurnEvent> RunAsync
    (
        AgentRunContext context,
        WorkflowCheckpoint? checkpoint,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        _ = checkpoint; // Always null in PR #2 (no-op store); PR #3 resumes from it here.

        Workflow workflow = BuildWorkflow(context);

        ResearchState input = ResearchState.Start(context.Task, FlattenHistory(context));

        StreamingRun run = await InProcessExecution.RunStreamingAsync
        (
            workflow,
            input,
            cancellationToken: cancellationToken
        );

        await foreach (WorkflowEvent workflowEvent in run.WatchStreamAsync(cancellationToken))
        {
            switch (workflowEvent)
            {
                case ResearchProgressEvent progress:
                    yield return new AgentActivityEvent
                    (
                        TurnId: context.TurnId,
                        Sequence: progress.Progress.Sequence,
                        Kind: progress.Progress.Kind,
                        Type: progress.Progress.Type,
                        Title: progress.Progress.Title,
                        DetailJson: progress.Progress.DetailJson
                    );
                    break;

                case ResearchUsageEvent usage:
                    yield return new UsageEvent
                    (
                        TurnId: context.TurnId,
                        Model: context.ExternalModelId,
                        InputTokens: usage.InputTokens,
                        OutputTokens: usage.OutputTokens
                    );
                    break;

                case WorkflowOutputEvent output:
                    // The whole report as ONE final TokenEvent (spec decision 4): the orchestrator
                    // accumulates token text into the completed message.
                    yield return new TokenEvent(context.TurnId, output.Data?.ToString() ?? string.Empty);
                    break;
            }
        }
    }

    private Workflow BuildWorkflow(AgentRunContext context)
    {
        var chatClient = _client.GetChatClient(context.ExternalModelId);

        AIAgent planner = chatClient.AsAIAgent(instructions: ResearchPrompts.PlannerInstructions);
        AIAgent condenser = chatClient.AsAIAgent(instructions: ResearchPrompts.CondenserInstructions);
        AIAgent critic = chatClient.AsAIAgent(instructions: ResearchPrompts.CriticInstructions);
        AIAgent writer = chatClient.AsAIAgent(instructions: ResearchPrompts.WriterInstructions);

        PlannerExecutor plannerExecutor = new(planner);
        SearchExecutor searchExecutor = new(_searchClient, _research);
        ReadExecutor readExecutor = new(_urlReader, condenser, _research);
        CriticExecutor criticExecutor = new(critic, _research);
        WriterExecutor writerExecutor = new(writer);

        return new WorkflowBuilder(plannerExecutor)
            .AddEdge(plannerExecutor, searchExecutor)
            .AddEdge(searchExecutor, readExecutor)
            .AddEdge(readExecutor, criticExecutor)
            .AddEdge<ResearchState>(criticExecutor, searchExecutor, condition: state => state!.OpenQuestions.Count > 0)
            .AddEdge<ResearchState>(criticExecutor, writerExecutor, condition: state => state!.OpenQuestions.Count == 0)
            .WithOutputFrom(writerExecutor)
            .Build();
    }

    private static IReadOnlyList<string> FlattenHistory(AgentRunContext context) =>
        context.PriorConversation
            .Select(message => $"{(message.Role == TurnRole.User ? "user" : "assistant")}: {message.Text}")
            .ToList();
}
```

Alignment notes (same discipline as Task 10 Step 1; structure and seams stay identical):
- `RunStreamingAsync` parameter order and the conditional `AddEdge` overload shape.
- `WorkflowOutputEvent.Data` access (`.Data` vs a typed accessor like `.As<string>()`).
- If `WatchStreamAsync` surfaces a workflow-error event type instead of throwing, detect it and `throw new InvalidOperationException(<its message>)` so the orchestrator's failure path engages.

- [ ] **Step 2: Register the runner and its options in `DependencyInjection.cs`**

Add usings:

```csharp
using Chat.Application.Abstractions.WebRead;
using Chat.Application.Abstractions.WebSearch;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Infrastructure.Agents.Research;
```

(Skip any already present.) Then extend `AddAgentRunPipeline` (Task 9) — append before the `return services;`:

```csharp
        services
            .AddOptions<ResearchOptions>()
            .Bind(configuration.GetSection(ResearchOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // A new agent kind = its own runner + one keyed registration here. Nothing else changes.
        services.AddKeyedScoped<IAgentRunRunner, ResearchWorkflowRunner>(AgentRunKind.Research);
```

> The worker's `AddTurnPipeline` already registers `IWebSearchClient` (Exa HttpClient) and `IUrlReader` (Firecrawl HttpClient) — the runner reuses those registrations. Verify both are registered in the worker composition (they are part of `AddTurnPipeline`, which `AddTurnWorkerInfrastructure` calls); if the research runner ever moves to a host without `AddTurnPipeline`, move those HttpClient registrations into `AddAgentRunPipeline`.

- [ ] **Step 3: Add research configuration** — in `src/services/Chat/Chat.TurnWorker/appsettings.json`, next to the `AgentRuns` section:

```json
  "Research": {
    "MaxRounds": 3,
    "MaxSearches": 12,
    "MaxSourcesToRead": 10
  }
```

- [ ] **Step 4: Build**

Run: `dotnet build Nova.slnx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure src/services/Chat/Chat.TurnWorker
git commit -m "feat(agents): add research workflow runner with keyed registration"
```

---

## Task 12: Read model — message `kind` + derived agent-run summary

`GetChat` messages gain `kind`; `AgentRun` messages gain a compact summary **derived** from `agent_runs` + activity counts grouped by `type` (never stored — spec §9). Shared chats expose `kind` only. No unit tests (Dapper readers + response mapping; project rule) — verified in Task 15.

**Files:**
- Modify: `src/services/Chat/Chat.Application/Chats/Queries/GetChat/ChatMessageReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChat/AgentRunSummaryReadModel.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Chats/Readers/ChatDetailReader.cs`
- Modify: `src/services/Chat/Chat.Api/Endpoints/Chats/GetChat/MessageResponse.cs`
- Modify: `src/services/Chat/Chat.Api/Endpoints/Chats/GetChat/ResponseMapper.cs`
- Modify: `src/services/Chat/Chat.Application/SharedChats/Queries/GetPublicSharedChat/PublicSharedChatMessageReadModel.cs` (kind only)
- Modify: `src/services/Chat/Chat.Infrastructure/SharedChats/Readers/PublicSharedChatReader.cs` (kind only)
- Modify: `src/services/Chat/Chat.Api/Endpoints/SharedChats/GetSharedChat/MessageResponse.cs` + its `ResponseMapper.cs` (kind only)

**Interfaces:**
- Consumes: `chat_messages.kind` (Task 2), `agent_runs` / `agent_run_activities` tables (existing, PR #1).
- Produces: `ChatMessageReadModel` with `string Kind` + `AgentRunSummaryReadModel? AgentRun` appended as the last two positional parameters; `AgentRunSummaryReadModel(string Kind, string? CurrentPhase, IReadOnlyDictionary<string, int> ActivityCounts, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt)`.

- [ ] **Step 1: Extend the read models**

`ChatMessageReadModel.cs` — append two positional parameters at the END of the record (after `Model`):

```csharp
public sealed record ChatMessageReadModel
(
    Guid Id,
    Guid? ParentMessageId,
    MessageRole Role,
    string? Content,
    MessageStatus Status,
    string? FailureReason,
    int SiblingIndex,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    ChatMessageModelReadModel? Model,
    MessageKind Kind,
    AgentRunSummaryReadModel? AgentRun
);
```

New `AgentRunSummaryReadModel.cs`:

```csharp
namespace Chat.Application.Chats.Queries.GetChat;

/// <summary>
/// Compact agent-run card summary, fully DERIVED at read time (never stored):
/// counts group activities by their kind-owned ActivityType (e.g. "web.search": 12, "source": 8);
/// CurrentPhase is the latest Phase activity's title. Null on branched/remixed copies without a run.
/// </summary>
public sealed record AgentRunSummaryReadModel
(
    string Kind,
    string? CurrentPhase,
    IReadOnlyDictionary<string, int> ActivityCounts,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt
);
```

- [ ] **Step 2: Extend `ChatDetailReader`**

1. In the message select of the `Sql` constant, add one column after `m.completed_at`:

```
       m.kind             as "Kind",
```

2. Append two more result sets at the end of the `Sql` constant (after the messages select's closing `;`):

```
select r.assistant_message_id as "AssistantMessageId",
       r.kind                 as "Kind",
       r.started_at           as "StartedAt",
       r.finished_at          as "FinishedAt",
       (
           select a.title
           from agent_run_activities a
           where a.run_id = r.id and a.kind = 'Phase'
           order by a.sequence desc
           limit 1
       )                      as "CurrentPhase"
from agent_runs r
where r.chat_id = @ChatId;

select r.assistant_message_id as "AssistantMessageId",
       a.type                 as "Type",
       count(*)::int          as "Count"
from agent_run_activities a
join agent_runs r on r.id = a.run_id
where r.chat_id = @ChatId
group by r.assistant_message_id, a.type;
```

3. Add `string Kind` to the `MessageRow` record (after `CompletedAt`), and two new private row records:

```csharp
    private sealed record RunSummaryRow
    (
        Guid AssistantMessageId,
        string Kind,
        DateTime StartedAt,
        DateTime? FinishedAt,
        string? CurrentPhase
    );

    private sealed record ActivityCountRow(Guid AssistantMessageId, string Type, int Count);
```

4. In `GetAsync`, after reading `MessageRow[] rows`, read and index the new sets:

```csharp
        RunSummaryRow[] runRows = (await grid.ReadAsync<RunSummaryRow>()).ToArray();
        ActivityCountRow[] countRows = (await grid.ReadAsync<ActivityCountRow>()).ToArray();

        Dictionary<Guid, Dictionary<string, int>> countsByMessage = countRows
            .GroupBy(row => row.AssistantMessageId)
            .ToDictionary
            (
                group => group.Key,
                group => group.ToDictionary(row => row.Type, row => row.Count)
            );

        Dictionary<Guid, AgentRunSummaryReadModel> summariesByMessage = runRows.ToDictionary
        (
            row => row.AssistantMessageId,
            row => new AgentRunSummaryReadModel
            (
                Kind: row.Kind,
                CurrentPhase: row.CurrentPhase,
                ActivityCounts: countsByMessage.TryGetValue(row.AssistantMessageId, out Dictionary<string, int>? counts)
                    ? counts
                    : new Dictionary<string, int>(),
                StartedAt: row.StartedAt,
                FinishedAt: row.FinishedAt
            )
        );
```

5. Extend the message projection with the two new arguments:

```csharp
                Kind: Enum.Parse<MessageKind>(row.Kind),
                AgentRun: summariesByMessage.TryGetValue(row.Id, out AgentRunSummaryReadModel? summary)
                    ? summary
                    : null
```

- [ ] **Step 3: Extend the GetChat API response**

`MessageResponse.cs` — add two members:

```csharp
    public required string Kind { get; init; }

    public required AgentRunSummaryResponse? AgentRun { get; init; }
```

and in the same file (below the class) add:

```csharp
internal sealed class AgentRunSummaryResponse
{
    public required string Kind { get; init; }

    public required string? CurrentPhase { get; init; }

    public required IReadOnlyDictionary<string, int> ActivityCounts { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset? FinishedAt { get; init; }
}
```

`ResponseMapper.ToMessage` — add the two assignments (kind lowercased like `Role`/`Status`, so the wire values are `"text"` / `"agentrun"`):

```csharp
        Kind = message.Kind.ToString().ToLowerInvariant(),
        AgentRun = message.AgentRun is null
            ? null
            : new AgentRunSummaryResponse
            {
                Kind = message.AgentRun.Kind.ToLowerInvariant(),
                CurrentPhase = message.AgentRun.CurrentPhase,
                ActivityCounts = message.AgentRun.ActivityCounts,
                StartedAt = message.AgentRun.StartedAt,
                FinishedAt = message.AgentRun.FinishedAt
            },
```

- [ ] **Step 4: Expose `kind` on shared chats (content only — never summaries or activities, spec §9)**

Mechanical pattern, four files:

1. `PublicSharedChatMessageReadModel.cs`: append a `string Kind` positional parameter at the end of the record.
2. `PublicSharedChatReader.cs`: the SQL selects message columns in three places (the recursive CTE's anchor select around line 29, the recursive arm around line 48, and the final projection around line 64). Add `message.kind` / `parent.kind` / `kind as "Kind"` respectively (match each list's alias style exactly), add `string Kind` to the reader's private message row record, and map `Kind: row.Kind` where the read model is constructed. Note: the CTE's `select` lists must stay column-aligned between anchor and recursive arm — add `kind` at the same position in both.
3. `Chat.Api/Endpoints/SharedChats/GetSharedChat/MessageResponse.cs`: add `public required string Kind { get; init; }`.
4. Its `ResponseMapper.cs`: add `Kind = message.Kind.ToLowerInvariant(),` in the message mapping (the read model's Kind is the raw enum string from the DB, e.g. `"AgentRun"`).

- [ ] **Step 5: Build and run the full test suite** (existing GetChat/SharedChat application tests must still compile against the extended records; fix their construction sites by appending `Kind: MessageKind.Text, AgentRun: null` / `Kind: "Text"` where needed)

```bash
dotnet build Nova.slnx
dotnet test
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/services/Chat tests/Chat
git commit -m "feat(agents): expose message kind and derived agent run summary in read models"
```

---

## Task 13: Application + API — agent-run detail endpoint

`GET /v1/chats/{chatId}/messages/{messageId}/agent-run` — owner-only full card data: summary + ordered activities. Deliberate exception, documented here: the handler reads through `IAgentRunRepository` (the aggregate with its ordered activities IS the read shape — tens of rows) instead of a Dapper reader; introduce a reader only if this ever needs columns the aggregate lacks.

**Files:**
- Create: `src/services/Chat/Chat.Application/AgentRuns/Queries/GetAgentRun/GetAgentRunQuery.cs`
- Create: `src/services/Chat/Chat.Application/AgentRuns/Queries/GetAgentRun/GetAgentRunQueryValidator.cs`
- Create: `src/services/Chat/Chat.Application/AgentRuns/Queries/GetAgentRun/AgentRunDetailResult.cs`
- Create: `src/services/Chat/Chat.Application/AgentRuns/Queries/GetAgentRun/GetAgentRunHandler.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/GetAgentRun/Endpoint.cs`
- Test: `tests/Chat/Chat.Application.Tests/AgentRuns/GetAgentRunHandlerTests.cs`

**Interfaces:**
- Consumes: `IAgentRunRepository.GetByAssistantMessageIdAsync`; `AgentRun.CurrentPhase` (computed); `AgentRunOperationErrors.NotFound` (Task 5); `FakeAgentRunRepository` (Task 7).
- Produces: `GetAgentRunQuery(Guid ChatId, Guid MessageId) : IQuery<ErrorOr<AgentRunDetailResult>>`.

- [ ] **Step 1: Write the failing handler tests**

```csharp
using Chat.Application.AgentRuns.Queries.GetAgentRun;
using Chat.Application.Tests.FavoriteModels;
using Chat.Domain.AgentRuns;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.AgentRuns;

public sealed class GetAgentRunHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeAgentRunRepository _runs = new();

    private AgentRun SeedRun(string userId = "auth0|user-1")
    {
        AgentRun run = AgentRun.Start
        (
            kind: AgentRunKind.Research,
            chatId: ChatId.New(),
            assistantMessageId: ChatMessageId.New(),
            userId: UserId.Create(userId).Value,
            task: AgentTask.Create("Research Redis Streams").Value,
            llmModelId: LlmModelId.New(),
            startedAt: Now
        );

        run.AppendActivity
        (
            sequence: ActivitySequence.Create(1).Value,
            kind: ActivityKind.Phase,
            type: ActivityType.Create("phase").Value,
            title: ActivityTitle.Create("Planning").Value,
            detail: null,
            occurredAt: Now
        );

        run.AppendActivity
        (
            sequence: ActivitySequence.Create(2).Value,
            kind: ActivityKind.ToolCall,
            type: ActivityType.Create("web.search").Value,
            title: ActivityTitle.Create("Searching: redis streams").Value,
            detail: ActivityDetail.Create("{\"query\":\"redis streams\"}").Value,
            occurredAt: Now
        );

        _runs.Seed(run);
        return run;
    }

    private GetAgentRunHandler CreateHandler(string userId = "auth0|user-1") =>
        new(userContext: new FakeUserContext(userId), runs: _runs);

    [Fact]
    public async Task Handle_ReturnsSummaryAndOrderedActivities()
    {
        AgentRun run = SeedRun();

        ErrorOr<AgentRunDetailResult> result = await CreateHandler()
            .Handle(new GetAgentRunQuery(run.ChatId.Value, run.AssistantMessageId.Value), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Research", result.Value.Kind);
        Assert.Equal("Research Redis Streams", result.Value.Task);
        Assert.Equal("Planning", result.Value.CurrentPhase);
        Assert.Equal(2, result.Value.Activities.Count);
        Assert.Equal(1, result.Value.Activities[0].Sequence);
        Assert.Equal("web.search", result.Value.Activities[1].Type);
        Assert.Equal("{\"query\":\"redis streams\"}", result.Value.Activities[1].Detail);
    }

    [Fact]
    public async Task Handle_WhenCallerIsNotTheOwner_ReturnsNotFound()
    {
        AgentRun run = SeedRun(userId: "auth0|owner");

        ErrorOr<AgentRunDetailResult> result = await CreateHandler(userId: "auth0|other")
            .Handle(new GetAgentRunQuery(run.ChatId.Value, run.AssistantMessageId.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("AgentRun.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenNoRunForMessage_ReturnsNotFound()
    {
        ErrorOr<AgentRunDetailResult> result = await CreateHandler()
            .Handle(new GetAgentRunQuery(Guid.CreateVersion7(), Guid.CreateVersion7()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("AgentRun.NotFound", result.FirstError.Code);
    }
}
```

- [ ] **Step 2: Run to verify compile failure**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~GetAgentRunHandlerTests"`
Expected: build error — query types not found.

- [ ] **Step 3: Create the query, validator, result, handler**

`GetAgentRunQuery.cs`:

```csharp
using ErrorOr;

using Mediator;

namespace Chat.Application.AgentRuns.Queries.GetAgentRun;

public sealed record GetAgentRunQuery(Guid ChatId, Guid MessageId) : IQuery<ErrorOr<AgentRunDetailResult>>;
```

`GetAgentRunQueryValidator.cs`:

```csharp
using FluentValidation;

namespace Chat.Application.AgentRuns.Queries.GetAgentRun;

internal sealed class GetAgentRunQueryValidator : AbstractValidator<GetAgentRunQuery>
{
    public GetAgentRunQueryValidator()
    {
        RuleFor(x => x.ChatId)
            .NotEmpty();

        RuleFor(x => x.MessageId)
            .NotEmpty();
    }
}
```

`AgentRunDetailResult.cs`:

```csharp
namespace Chat.Application.AgentRuns.Queries.GetAgentRun;

public sealed record AgentRunActivityResult
(
    int Sequence,
    string Kind,
    string Type,
    string Title,
    string? Detail,
    DateTimeOffset OccurredAt
);

public sealed record AgentRunUsageResult(int InputTokens, int OutputTokens);

public sealed record AgentRunDetailResult
(
    string Kind,
    string Task,
    string? CurrentPhase,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    AgentRunUsageResult Usage,
    IReadOnlyList<AgentRunActivityResult> Activities
);
```

`GetAgentRunHandler.cs`:

```csharp
using Chat.Application.AgentRuns.Errors;
using Chat.Domain.AgentRuns;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.AgentRuns.Queries.GetAgentRun;

internal sealed class GetAgentRunHandler(IUserContext userContext, IAgentRunRepository runs)
    : IQueryHandler<GetAgentRunQuery, ErrorOr<AgentRunDetailResult>>
{
    public async ValueTask<ErrorOr<AgentRunDetailResult>> Handle(GetAgentRunQuery query, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(query.ChatId);
        ErrorOr<ChatMessageId> messageIdResult = ChatMessageId.Create(query.MessageId);

        if (userIdResult.IsError || chatIdResult.IsError || messageIdResult.IsError)
        {
            // Malformed ids cannot correspond to a run; same NotFound as absence (no leak).
            return Error.NotFound(code: "AgentRun.NotFound", description: "No agent run found.");
        }

        AgentRun? run = await runs.GetByAssistantMessageIdAsync(messageIdResult.Value, cancellationToken);

        // Owner + chat scoping; a mismatch is indistinguishable from absence (no information leak).
        if (run is null || run.UserId != userIdResult.Value || run.ChatId != chatIdResult.Value)
        {
            return AgentRunOperationErrors.NotFound(messageIdResult.Value);
        }

        List<AgentRunActivityResult> activities = run.Activities
            .OrderBy(activity => activity.Sequence.Value)
            .Select(activity => new AgentRunActivityResult
            (
                Sequence: activity.Sequence.Value,
                Kind: activity.Kind.ToString(),
                Type: activity.Type.Value,
                Title: activity.Title.Value,
                Detail: activity.Detail?.Value,
                OccurredAt: activity.OccurredAt
            ))
            .ToList();

        return new AgentRunDetailResult
        (
            Kind: run.Kind.ToString(),
            Task: run.Task.Value,
            CurrentPhase: run.CurrentPhase?.Value,
            StartedAt: run.StartedAt,
            FinishedAt: run.FinishedAt,
            Usage: new AgentRunUsageResult(run.Usage.InputTokens, run.Usage.OutputTokens),
            Activities: activities
        );
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~GetAgentRunHandlerTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Create the endpoint** (`Endpoints/Chats/GetAgentRun/Endpoint.cs`):

```csharp
using Chat.Application.AgentRuns.Queries.GetAgentRun;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.GetAgentRun;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest
{
    public const string RouteName = "Chat.Chats.GetAgentRun";

    public override void Configure()
    {
        Get("/chats/{chatId}/messages/{messageId}/agent-run");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Get Agent Run")
                .WithDescription("Returns the agent run behind an assistant card message: summary, usage, and the full ordered activity log. Owner-only.")
                .Produces<AgentRunDetailResult>(StatusCodes.Status200OK)
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        GetAgentRunQuery query = new
        (
            ChatId: Route<Guid>("chatId"),
            MessageId: Route<Guid>("messageId")
        );

        ErrorOr<AgentRunDetailResult> result = await sender.Send(query, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
```

- [ ] **Step 6: Build, then commit**

```bash
dotnet build src/services/Chat/Chat.Api
git add src/services/Chat tests/Chat
git commit -m "feat(agents): add owner-only agent run detail query and endpoint"
```

---

## Task 14: API — start-research endpoints

`POST /v1/chats/research` (201) and `POST /v1/chats/{chatId}/research` (202), mirroring the CreateChat/SendMessage endpoint pair and reusing `TurnStartedResponse` (its `StreamPath` already points at the Task 4 endpoint).

**Files:**
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/CreateResearchChat/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/StartResearch/Endpoint.cs`

**Interfaces:**
- Consumes: `CreateResearchChatCommand`/`StartResearchCommand` (Task 7); `TurnStartedResponse` (existing, `Chat.Api.Endpoints.Chats.Responses`).

- [ ] **Step 1: Create the CreateResearchChat endpoint**

```csharp
using Chat.Api.Endpoints.Chats.Responses;
using Chat.Application.Chats.Commands.CreateResearchChat;
using Chat.Application.Chats.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.CreateResearchChat;

internal sealed record Request(string Task, Guid LlmModelId, Guid? ProjectId = null);

internal sealed class Endpoint(ISender sender) : Endpoint<Request>
{
    public const string RouteName = "Chat.Chats.CreateResearchChat";

    public override void Configure()
    {
        Post("/chats/research");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Create Research Chat")
                .WithDescription("Creates a chat whose first turn is a deep-research agent run; progress streams on the returned stream path and the report arrives as the assistant message.")
                .Produces<TurnStartedResponse>(StatusCodes.Status201Created)
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .ProducesProblemDetails(StatusCodes.Status409Conflict, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        CreateResearchChatCommand command = new(request.Task, request.LlmModelId, request.ProjectId);

        ErrorOr<TurnStartedResult> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        TurnStartedResponse response = TurnStartedResponse.From(result.Value);

        await Send.ResultAsync(TypedResults.Created($"/v1/chats/{response.ChatId}", response));
    }
}
```

- [ ] **Step 2: Create the StartResearch endpoint**

```csharp
using Chat.Api.Endpoints.Chats.Responses;
using Chat.Application.Chats.Commands.StartResearch;
using Chat.Application.Chats.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.StartResearch;

internal sealed record Request(string Task, Guid LlmModelId);

internal sealed class Endpoint(ISender sender) : Endpoint<Request>
{
    public const string RouteName = "Chat.Chats.StartResearch";

    public override void Configure()
    {
        Post("/chats/{chatId}/research");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Start Research")
                .WithDescription("Starts a deep-research agent run on the active branch of an existing chat. Rejected on temporary chats and while a turn is still generating.")
                .Produces<TurnStartedResponse>(StatusCodes.Status202Accepted)
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .ProducesProblemDetails(StatusCodes.Status409Conflict, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        StartResearchCommand command = new(Route<Guid>("chatId"), request.Task, request.LlmModelId);

        ErrorOr<TurnStartedResult> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResultAsync(TypedResults.Accepted((string?)null, TurnStartedResponse.From(result.Value)));
    }
}
```

- [ ] **Step 3: Build, then commit**

```bash
dotnet build src/services/Chat/Chat.Api
git add src/services/Chat/Chat.Api
git commit -m "feat(agents): add research start endpoints"
```

---

## Task 15: Full verification

- [ ] **Step 1: Build everything and run every test project**

```bash
dotnet build Nova.slnx
dotnet test
```

Expected: clean build; all domain and application tests pass.

- [ ] **Step 2: Verify EF model state** — this plan's only migration is Task 2's:

```bash
dotnet ef migrations has-pending-model-changes \
  --project src/services/Chat/Chat.Infrastructure \
  --startup-project src/workers/Chat.MigrationWorker
```

Expected: no pending model changes. Pending changes mean something added state the spec forbids — stop and review.

- [ ] **Step 3: End-to-end smoke test** (manual; needs the configured OpenRouter/Exa/Firecrawl keys). Start the stack (`aspire run` or `dotnet run --project Nova.AppHost`), then with a valid bearer token and an enabled model GUID:

```bash
# 1. Start research in a new chat — expect 201 with chatId/assistantMessageId/streamPath
curl -sk -X POST https://localhost:7201/v1/chats/research \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"task": "Research the current state of Redis Streams adoption.", "llmModelId": "<model-id>"}'

# 2. Attach SSE — expect agent_activity events (Phase/ToolCall/Observation/Thought),
#    usage events, then ONE large token event (the report), then done
curl -skN https://localhost:7201<streamPath> -H "Authorization: Bearer <token>"

# 3. Chat detail — the assistant message has kind "agentrun" and an agentRun summary
#    with currentPhase + activityCounts (e.g. "web.search": n, "source": n)
curl -sk https://localhost:7201/v1/chats/<chatId> -H "Authorization: Bearer <token>"

# 4. Full activity log — ordered activities, usage, finishedAt set
curl -sk https://localhost:7201/v1/chats/<chatId>/messages/<assistantMessageId>/agent-run \
  -H "Authorization: Bearer <token>"

# 5. Start a second run and stop it mid-flight — expect 202 on stop, a "stopped" SSE event,
#    message status "stopped" with null content, activities preserved in the detail endpoint
curl -sk -X POST https://localhost:7201/v1/chats/<chatId>/research \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"task": "Research something long enough to stop.", "llmModelId": "<model-id>"}'
curl -sk -X POST https://localhost:7201/v1/chats/<chatId>/messages/<newAssistantMessageId>/stop \
  -H "Authorization: Bearer <token>"

# 6. Guards: research on a temporary chat → 409 Chat.CannotStartAgentRunInTemporaryChat;
#    research while a turn is generating → 409 Chat.ParentStillGenerating;
#    regenerate on the research card → 409 Chat.CannotRegenerateAgentRun;
#    SSE for a normal chat turn → token events stream live (the dead reader now serves).
```

Also verify in the Aspire dashboard that `chat-turn-worker` stays healthy and the `agent-run-requested` queue exists in RabbitMQ with the 60-minute consumer timeout.

- [ ] **Step 4: Final commit if verification touched anything**

```bash
git add -A
git commit -m "chore(agents): verification fixes for the agent run pipeline"
```

---

## Spec Coverage Map

| Spec section | Task(s) |
| --- | --- |
| §4 MessageKind + guards (incl. decision 6 temporary chats) | 1, 2 |
| §5 Stream contract (`agent_activity`, report as final token) | 3, 8, 11 |
| §6.1 Job contract | 5, 7 |
| §6.2 Orchestrator + error contract + cancellation triage | 8 |
| §6.3 Seams (runner, resolver, checkpoint no-op, context builder) | 5, 6, 9 |
| §6.4 Start commands | 7 |
| §7 Messaging, dedicated queue, ack timeout, `AgentRunOptions` | 5, 9 |
| §8 Research runner, executors, activity vocabulary, `ResearchOptions` | 10, 11 |
| §9 SSE endpoint | 4 |
| §9 Start endpoints / detail endpoint / GetChat summary / shared kind / stop unchanged | 14 / 13 / 12 / 12 / — |
| §10 Races (redelivery, restart-from-scratch, delete-mid-run) | 8 (+ existing race-hardening behavior) |
| §11 Configuration | 9, 10, 11 |
| §12 Testing scope | 1, 3, 6, 7, 8, 13 |
| §13 / PR #3 deferrals | explicitly NOT here |

## Explicitly Out of Scope (do not "helpfully" add)

- Checkpoint persistence, MAF `CheckpointManager`, resume-on-redelivery, checkpoint purge job — **PR #3, its own design pass**. The seam and its no-op ship here; nothing more.
- Stop-and-synthesize, clarifying questions, report token streaming, regenerating agent cards, per-executor models, scheduled runs, memory, analytics decorator for agent runs, per-kind queues, activity logs on shared chats.
- Temporary-chat agent runs — permanently excluded by spec decision 6, not deferred.
- MassTransit upgrade (stays 8.4.1); any change to the existing chat-turn pipeline beyond the SSE endpoint it was always owed.




