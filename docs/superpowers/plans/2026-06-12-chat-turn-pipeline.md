# Chat Turn Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Asynchronous chat turn execution — the API persists a user message plus a `Generating` assistant placeholder and outbox-publishes a turn job to RabbitMQ; a separate `Chat.TurnWorker` runs the LLM turn via Microsoft Agent Framework and streams `TurnEvent`s through a per-turn Redis Stream; an SSE endpoint replays that stream to the client.

**Architecture:** Per the approved spec `docs/superpowers/specs/2026-06-12-chat-turn-pipeline-design.md`. The turn lifecycle **is** the assistant `ChatMessage` (`Generating → Completed/Failed`) — no new entities. RabbitMQ + MassTransit (existing EF Core bus outbox) carries the job; a short-lived Redis Stream (`chat:turn:{assistantMessageId}`) carries the token firehose with `Last-Event-ID` replay. The Agent Framework is quarantined inside `Chat.Infrastructure/Agents/`; everything else speaks `TurnEvent`. PostHog telemetry is a removable `IAgentRunner` decorator.

**Tech Stack:** .NET 10, Mediator.SourceGenerator, FastEndpoints, MassTransit 8.4.1 (pinned — do not upgrade), EF Core + Npgsql, StackExchange.Redis, Microsoft Agent Framework (`Microsoft.Agents.AI.*`), PostHog .NET SDK, ErrorOr, FluentValidation, xunit.

---

## ⚠️ Binding Architecture Rules — read before every task

The spec's rules are restated here because violating them recreates the coupled mess this design exists to prevent. **Check your work against these after every task:**

1. **Agent Framework types appear ONLY in `src/services/Chat/Chat.Infrastructure/Agents/`** (`AgentFrameworkRunner.cs`, `TurnEventMapper.cs`, `AgentOptions.cs`). If you type `using Microsoft.Agents.AI` or `using Microsoft.Extensions.AI` anywhere else — stop, you are doing it wrong.
2. **`ChatTurnOrchestrator` is sequencing only.** No business branching. New pipeline steps = new seam interface, never inline logic.
3. **Cross-cutting = decorator + one DI registration.** Deleting telemetry must be a 3-line DI deletion, nothing else.
4. **`IContextBuilder` assembles system prompt + history + memories. Nothing else.** Do not add parameters to it.
5. **No tools in this pass.** `ToolCallEvent`/`ToolResultEvent` exist in the contract only.
6. **`TurnEvent` is append-only with explicit discriminators.** Never change an existing event's shape.
7. **All state transitions go through the `ChatThread` aggregate.** Never set `Status` via SQL or bypass `CompleteAssistantMessage`/`FailAssistantMessage`.
8. **No memory implementation.** `NoOpMemoryRetriever` stays a no-op.

## Ground Rules (project conventions)

- `Mediator.SourceGenerator` family — never MediatR. FastEndpoints — never controllers. MassTransit stays at 8.4.1.
- Test work in this plan was explicitly approved in the spec (§5 Testing Strategy). Do not expand testing beyond what each task specifies without asking the user.
- Handlers/consumers/repositories are `internal sealed`. Value-object factory methods return `ErrorOr<T>`. Follow the surrounding code style (named arguments, expression-bodied where the codebase does it).
- Commit after every task. Build must pass before each commit.
- Run all commands from the repo root `/Users/akakijomidava/RiderProjects/Nova`.

## File Structure Overview

```
src/services/Chat/
  Chat.Domain/Chats/                       (Task 1: one guard added)
  Chat.Application/
    Abstractions/Analytics/IAnalytics.cs                  (Task 2)
    Abstractions/Turns/{IAgentRunner,ITokenPublisher,ITurnStreamReader,
                        IContextBuilder,IMemoryRetriever,TurnContext,
                        RetrievedMemories}.cs              (Task 2)
    Turns/{TurnEvent,TurnEventSerializer,TurnRequested,TurnErrors,
           NoOpMemoryRetriever,ContextBuilder,
           ChatTurnOrchestrator,TelemetryAgentRunner}.cs   (Tasks 2,3,5,8)
    Chats/{Errors/ChatOperationErrors.cs, ModelUsability.cs,
           Results/TurnStartedResult.cs,
           Commands/CreateChat/*, Commands/SendMessage/*}  (Task 4)
  Chat.Infrastructure/
    Turns/{RedisStreamTokenPublisher,RedisTurnStreamReader}.cs (Task 6)
    Turns/Consumers/{TurnRequestedConsumer,
                     TurnRequestedConsumerDefinition}.cs   (Task 9)
    Agents/{AgentOptions,TurnEventMapper,AgentFrameworkRunner}.cs (Task 7)
    Analytics/{PostHogAnalytics,NullAnalytics}.cs          (Task 8)
    DependencyInjection.cs                                 (Task 9: worker + reader wiring)
  Chat.TurnWorker/{Chat.TurnWorker.csproj,Program.cs,appsettings.json} (Task 10)
  Chat.Api/Endpoints/Chats/{CreateChat,SendMessage,StreamTurn}/* (Task 11)
tests/Chat/
  Chat.Domain.Tests/Chats/ChatThreadTurnGuardTests.cs      (Task 1)
  Chat.Application.Tests/Turns/*                           (Tasks 2,3,4,5,8)
  Chat.Infrastructure.Tests/ (new project)                  (Task 7)
Nova.AppHost/{AppHost.cs,Nova.AppHost.csproj}              (Task 10)
```

---

## Task 1: Domain guard — no replies under a generating assistant

This is the per-conversation concurrency guard from the spec: you cannot start a new turn while the head assistant message is still `Generating`.

**Files:**
- Modify: `src/services/Chat/Chat.Domain/Chats/ChatErrors.cs`
- Modify: `src/services/Chat/Chat.Domain/Chats/ChatThread.cs` (inside `AddUserMessage`, after the role check at ~line 101)
- Test: `tests/Chat/Chat.Domain.Tests/Chats/ChatThreadTurnGuardTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Domain.Tests.Chats;

public sealed class ChatThreadTurnGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AddUserMessage_WhenParentAssistantIsGenerating_ReturnsParentStillGenerating()
    {
        ChatThread thread = ChatThread.Create
        (
            userId: UserId.Create("auth0|user-1").Value,
            title: ChatTitle.Create("Hello").Value,
            firstUserMessage: MessageContent.Create("Hello").Value,
            createdAt: Now
        );

        ErrorOr<ChatMessage> assistant = thread.BeginAssistantMessage
        (
            parentMessageId: thread.CurrentMessageId,
            llmModelId: null,
            createdAt: Now
        );

        Assert.False(assistant.IsError);

        ErrorOr<ChatMessage> reply = thread.AddUserMessage
        (
            parentMessageId: assistant.Value.Id,
            content: MessageContent.Create("Too eager").Value,
            createdAt: Now
        );

        Assert.True(reply.IsError);
        Assert.Equal("Chat.ParentStillGenerating", reply.FirstError.Code);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Chat/Chat.Domain.Tests --filter "FullyQualifiedName~ChatThreadTurnGuardTests"`
Expected: FAIL (the reply currently succeeds, so the `IsError` assertion fails).

- [ ] **Step 3: Add the error to `ChatErrors.cs`** (append inside the class)

```csharp
    public static Error ParentStillGenerating(ChatMessageId parentMessageId) =>
        Error.Conflict
        (
            code: "Chat.ParentStillGenerating",
            description:
            $"Message '{parentMessageId.Value}' is still generating; wait for the turn to finish before replying."
        );
```

- [ ] **Step 4: Add the guard in `ChatThread.AddUserMessage`**, immediately after the `parent.Role != MessageRole.Assistant` check:

```csharp
        if (parent.Status == MessageStatus.Generating)
        {
            return ChatErrors.ParentStillGenerating(parentMessageId);
        }
```

- [ ] **Step 5: Run the test to verify it passes, then run the whole domain suite**

Run: `dotnet test tests/Chat/Chat.Domain.Tests`
Expected: PASS, no regressions.

- [ ] **Step 6: Commit**

```bash
git add src/services/Chat/Chat.Domain tests/Chat/Chat.Domain.Tests
git commit -m "feat(chat): reject user replies under a still-generating assistant message"
```

---

## Task 2: Turn contracts and seams

The vocabulary of the whole pipeline. **Rule 6 applies from this moment: append-only.**

**Files:**
- Create: `src/services/Chat/Chat.Application/Turns/TurnEvent.cs`
- Create: `src/services/Chat/Chat.Application/Turns/TurnEventSerializer.cs`
- Create: `src/services/Chat/Chat.Application/Turns/TurnRequested.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/Turns/TurnContext.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/Turns/RetrievedMemories.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/Turns/IAgentRunner.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/Turns/ITokenPublisher.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/Turns/ITurnStreamReader.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/Turns/IContextBuilder.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/Turns/IMemoryRetriever.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/Analytics/IAnalytics.cs`
- Create: `src/services/Chat/Chat.Application/Turns/NoOpMemoryRetriever.cs`
- Test: `tests/Chat/Chat.Application.Tests/Turns/TurnEventSerializerTests.cs`

- [ ] **Step 1: Write the failing serializer round-trip test**

```csharp
using Chat.Application.Turns;

namespace Chat.Application.Tests.Turns;

public sealed class TurnEventSerializerTests
{
    private static readonly Guid TurnId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static TheoryData<TurnEvent, string> Events => new()
    {
        { new TokenEvent(TurnId, "Hello"), "token" },
        { new ToolCallEvent(TurnId, "search", "{}"), "tool_call" },
        { new ToolResultEvent(TurnId, "search", "3 results"), "tool_result" },
        { new UsageEvent(TurnId, "gpt-4.1", 120, 45), "usage" },
        { new DoneEvent(TurnId), "done" },
        { new FailedEvent(TurnId, "provider timeout"), "failed" }
    };

    [Theory]
    [MemberData(nameof(Events))]
    public void RoundTrips_EveryEventType_WithStableDiscriminator(TurnEvent original, string expectedName)
    {
        string json = TurnEventSerializer.Serialize(original);

        Assert.Contains($"\"type\":\"{expectedName}\"", json);
        Assert.Equal(expectedName, TurnEventSerializer.EventName(original));

        TurnEvent? deserialized = TurnEventSerializer.Deserialize(json);

        Assert.Equal(original, deserialized);
    }
}
```

- [ ] **Step 2: Run it to verify it fails to compile** (types don't exist yet)

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~TurnEventSerializerTests"`
Expected: build error — `TurnEvent` not found.

- [ ] **Step 3: Create `TurnEvent.cs`**

```csharp
using System.Text.Json.Serialization;

namespace Chat.Application.Turns;

/// <summary>
/// The streaming vocabulary of a chat turn. APPEND-ONLY (spec Rule 6):
/// new derived events may be added; existing shapes and discriminators must never change.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TokenEvent), "token")]
[JsonDerivedType(typeof(ToolCallEvent), "tool_call")]
[JsonDerivedType(typeof(ToolResultEvent), "tool_result")]
[JsonDerivedType(typeof(UsageEvent), "usage")]
[JsonDerivedType(typeof(DoneEvent), "done")]
[JsonDerivedType(typeof(FailedEvent), "failed")]
public abstract record TurnEvent(Guid TurnId);

public sealed record TokenEvent(Guid TurnId, string Text) : TurnEvent(TurnId);

public sealed record ToolCallEvent(Guid TurnId, string Tool, string ArgsJson) : TurnEvent(TurnId);

public sealed record ToolResultEvent(Guid TurnId, string Tool, string Summary) : TurnEvent(TurnId);

public sealed record UsageEvent(Guid TurnId, string Model, int InputTokens, int OutputTokens) : TurnEvent(TurnId);

public sealed record DoneEvent(Guid TurnId) : TurnEvent(TurnId);

public sealed record FailedEvent(Guid TurnId, string Reason) : TurnEvent(TurnId);
```

- [ ] **Step 4: Create `TurnEventSerializer.cs`**

```csharp
using System.Text.Json;

namespace Chat.Application.Turns;

public static class TurnEventSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(TurnEvent turnEvent) =>
        JsonSerializer.Serialize(turnEvent, Options);

    public static TurnEvent? Deserialize(string json) =>
        JsonSerializer.Deserialize<TurnEvent>(json, Options);

    public static string EventName(TurnEvent turnEvent) => turnEvent switch
    {
        TokenEvent => "token",
        ToolCallEvent => "tool_call",
        ToolResultEvent => "tool_result",
        UsageEvent => "usage",
        DoneEvent => "done",
        FailedEvent => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(turnEvent), turnEvent.GetType().Name, "Unknown turn event type.")
    };
}
```

- [ ] **Step 5: Create `TurnRequested.cs`** (the MassTransit job message — ids only; the worker loads state from the database and never trusts payload state)

```csharp
namespace Chat.Application.Turns;

public sealed record TurnRequested(Guid ChatId, string UserId, Guid AssistantMessageId);
```

- [ ] **Step 6: Create the seam interfaces and context types**

`Abstractions/Turns/TurnContext.cs`:

```csharp
namespace Chat.Application.Abstractions.Turns;

public enum TurnRole
{
    User = 1,
    Assistant = 2
}

public sealed record TurnMessage(TurnRole Role, string Text);

public sealed record TurnContext
(
    Guid TurnId,
    Guid ChatId,
    string UserId,
    string ExternalModelId,
    string SystemPrompt,
    IReadOnlyList<TurnMessage> Messages
);
```

`Abstractions/Turns/RetrievedMemories.cs`:

```csharp
namespace Chat.Application.Abstractions.Turns;

public sealed record RetrievedMemories(IReadOnlyList<string> Items)
{
    public static readonly RetrievedMemories Empty = new([]);
}
```

`Abstractions/Turns/IAgentRunner.cs`:

```csharp
using Chat.Application.Turns;

namespace Chat.Application.Abstractions.Turns;

public interface IAgentRunner
{
    IAsyncEnumerable<TurnEvent> RunAsync(TurnContext context, CancellationToken cancellationToken);
}
```

`Abstractions/Turns/ITokenPublisher.cs`:

```csharp
using Chat.Application.Turns;

namespace Chat.Application.Abstractions.Turns;

public interface ITokenPublisher
{
    Task PublishAsync(TurnEvent turnEvent, CancellationToken cancellationToken);

    /// <summary>Deletes any partial stream left behind by a crashed previous attempt.</summary>
    Task ResetAsync(Guid turnId, CancellationToken cancellationToken);
}
```

`Abstractions/Turns/ITurnStreamReader.cs`:

```csharp
using Chat.Application.Turns;

namespace Chat.Application.Abstractions.Turns;

public sealed record TurnStreamEntry(string EntryId, TurnEvent Event);

public interface ITurnStreamReader
{
    /// <summary>
    /// Reads turn events from the per-turn stream starting after <paramref name="fromEntryId"/>
    /// (or from the beginning when null), completing after a done/failed event.
    /// </summary>
    IAsyncEnumerable<TurnStreamEntry> ReadAsync(Guid turnId, string? fromEntryId, CancellationToken cancellationToken);
}
```

`Abstractions/Turns/IContextBuilder.cs`:

```csharp
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;

using ErrorOr;

namespace Chat.Application.Abstractions.Turns;

public interface IContextBuilder
{
    Task<ErrorOr<TurnContext>> BuildAsync
    (
        ChatThread thread,
        ChatMessage assistantMessage,
        RetrievedMemories memories,
        CancellationToken cancellationToken
    );
}
```

`Abstractions/Turns/IMemoryRetriever.cs`:

```csharp
using Chat.Application.Turns;

namespace Chat.Application.Abstractions.Turns;

public interface IMemoryRetriever
{
    Task<RetrievedMemories> RetrieveAsync(TurnRequested job, CancellationToken cancellationToken);
}
```

`Abstractions/Analytics/IAnalytics.cs`:

```csharp
namespace Chat.Application.Abstractions.Analytics;

public interface IAnalytics
{
    void Capture(string distinctId, string eventName, Dictionary<string, object> properties);
}
```

- [ ] **Step 7: Create `NoOpMemoryRetriever.cs`** — spec Rule 8: this stays a no-op until a dedicated memory design session.

```csharp
using Chat.Application.Abstractions.Turns;

namespace Chat.Application.Turns;

/// <summary>
/// Deliberate no-op (spec Rule 8). Memory retrieval gets its own design later;
/// do NOT implement retrieval, embeddings, or extraction here.
/// </summary>
public sealed class NoOpMemoryRetriever : IMemoryRetriever
{
    public Task<RetrievedMemories> RetrieveAsync(TurnRequested job, CancellationToken cancellationToken) =>
        Task.FromResult(RetrievedMemories.Empty);
}
```

- [ ] **Step 8: Run the serializer test to verify it passes**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~TurnEventSerializerTests"`
Expected: PASS (6 theory cases).

- [ ] **Step 9: Commit**

```bash
git add src/services/Chat/Chat.Application tests/Chat/Chat.Application.Tests
git commit -m "feat(chat): add turn event contract and pipeline seam interfaces"
```

---

## Task 3: ContextBuilder

Walks the active branch from the assistant placeholder to the root and resolves the model's external id. **Rule 4: this class assembles system prompt + history + memories — nothing else, ever.**

**Files:**
- Create: `src/services/Chat/Chat.Application/Turns/TurnErrors.cs`
- Create: `src/services/Chat/Chat.Application/Turns/ContextBuilder.cs`
- Create: `tests/Chat/Chat.Application.Tests/Turns/FakeLlmProviderRepository.cs`
- Test: `tests/Chat/Chat.Application.Tests/Turns/ContextBuilderTests.cs`

- [ ] **Step 1: Create the fake repository.** First open `src/services/Chat/Chat.Domain/ModelCatalog/ILlmProviderRepository.cs` and copy its exact member list; only `GetByModelIdAsync` needs real behavior, all other members throw `NotSupportedException`. (If `tests/Chat/Chat.Application.Tests` already contains an `ILlmProviderRepository` fake from the FavoriteModels tests, reuse that instead and skip this file.)

```csharp
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.ValueObjects;

namespace Chat.Application.Tests.Turns;

internal sealed class FakeLlmProviderRepository : ILlmProviderRepository
{
    private readonly List<LlmProvider> _providers = [];

    public void Seed(LlmProvider provider) => _providers.Add(provider);

    public Task<LlmProvider?> GetByModelIdAsync(LlmModelId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_providers.FirstOrDefault(provider => provider.FindModel(id) is not null));

    // Implement every remaining ILlmProviderRepository member as:
    //   => throw new NotSupportedException();
}
```

- [ ] **Step 2: Write the failing tests.** Use the existing `TestCatalogFactory` (`tests/Chat/Chat.Application.Tests/ModelCatalog/TestCatalogFactory.cs`) to build a provider/model.

```csharp
using Chat.Application.Abstractions.Turns;
using Chat.Application.Tests.ModelCatalog;
using Chat.Application.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Turns;

public sealed class ContextBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeLlmProviderRepository _providers = new();

    private (ChatThread Thread, ChatMessage Assistant, LlmModel Model) CreateThreadWithPendingTurn()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        LlmModel model = provider.AddModel
        (
            externalModelId: TestCatalogFactory.CreateExternalModelId("gpt-4.1"),
            profile: TestCatalogFactory.CreateProfile()
        ).Value;
        _providers.Seed(provider);

        ChatThread thread = ChatThread.Create
        (
            userId: UserId.Create("auth0|user-1").Value,
            title: ChatTitle.Create("Hello").Value,
            firstUserMessage: MessageContent.Create("What is Redis?").Value,
            createdAt: Now
        );

        ChatMessage assistant = thread.BeginAssistantMessage(thread.CurrentMessageId, model.Id, Now).Value;

        return (thread, assistant, model);
    }

    [Fact]
    public async Task BuildAsync_ProducesChronologicalHistory_EndingAtTheUserMessage()
    {
        (ChatThread thread, ChatMessage assistant, _) = CreateThreadWithPendingTurn();

        ContextBuilder builder = new(_providers);

        ErrorOr<TurnContext> context = await builder.BuildAsync(thread, assistant, RetrievedMemories.Empty, CancellationToken.None);

        Assert.False(context.IsError);
        Assert.Equal(assistant.Id.Value, context.Value.TurnId);
        Assert.Equal("gpt-4.1", context.Value.ExternalModelId);
        TurnMessage single = Assert.Single(context.Value.Messages);
        Assert.Equal(TurnRole.User, single.Role);
        Assert.Equal("What is Redis?", single.Text);
    }

    [Fact]
    public async Task BuildAsync_WhenModelIsUnknown_ReturnsModelNotFound()
    {
        (ChatThread thread, ChatMessage assistant, _) = CreateThreadWithPendingTurn();

        ContextBuilder builder = new(new FakeLlmProviderRepository());

        ErrorOr<TurnContext> context = await builder.BuildAsync(thread, assistant, RetrievedMemories.Empty, CancellationToken.None);

        Assert.True(context.IsError);
        Assert.Equal("Turn.ModelNotFound", context.FirstError.Code);
    }
}
```

- [ ] **Step 3: Run to verify compile failure**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~ContextBuilderTests"`
Expected: build error — `ContextBuilder` not found.

- [ ] **Step 4: Create `TurnErrors.cs`**

```csharp
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

namespace Chat.Application.Turns;

public static class TurnErrors
{
    public static Error ModelNotConfigured(ChatMessageId messageId) =>
        Error.Conflict
        (
            code: "Turn.ModelNotConfigured",
            description: $"Assistant message '{messageId.Value}' has no model assigned."
        );

    public static Error ModelNotFound(LlmModelId modelId) =>
        Error.NotFound
        (
            code: "Turn.ModelNotFound",
            description: $"No model found with id '{modelId.Value}'."
        );
}
```

- [ ] **Step 5: Create `ContextBuilder.cs`**

```csharp
using Chat.Application.Abstractions.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;

using ErrorOr;

namespace Chat.Application.Turns;

internal sealed class ContextBuilder(ILlmProviderRepository llmProviders) : IContextBuilder
{
    private const string DefaultSystemPrompt = "You are Nova, a helpful AI assistant.";

    public async Task<ErrorOr<TurnContext>> BuildAsync
    (
        ChatThread thread,
        ChatMessage assistantMessage,
        RetrievedMemories memories,
        CancellationToken cancellationToken
    )
    {
        _ = memories; // Reserved until the memory design lands (spec Rule 8).

        if (assistantMessage.LlmModelId is null)
        {
            return TurnErrors.ModelNotConfigured(assistantMessage.Id);
        }

        LlmProvider? provider = await llmProviders.GetByModelIdAsync(assistantMessage.LlmModelId, cancellationToken);
        LlmModel? model = provider?.FindModel(assistantMessage.LlmModelId);

        if (provider is null || model is null)
        {
            return TurnErrors.ModelNotFound(assistantMessage.LlmModelId);
        }

        List<TurnMessage> history = [];
        ChatMessageId? cursor = assistantMessage.ParentMessageId;

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

        return new TurnContext
        (
            TurnId: assistantMessage.Id.Value,
            ChatId: thread.Id.Value,
            UserId: thread.UserId.Value,
            ExternalModelId: model.ExternalModelId.Value,
            SystemPrompt: DefaultSystemPrompt,
            Messages: history
        );
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~ContextBuilderTests"`
Expected: PASS (2 tests).

- [ ] **Step 7: Commit**

```bash
git add src/services/Chat/Chat.Application tests/Chat/Chat.Application.Tests
git commit -m "feat(chat): add turn context builder walking the active branch"
```

---

## Task 4: CreateChat and SendMessage commands

Both persist the user message **and** a `Generating` assistant placeholder, then publish `TurnRequested` via `IMessageBus`. The publish happens **before** `SaveChangesAsync` on purpose: MassTransit's bus outbox buffers it and writes it to the outbox table inside the same transaction — this is the no-dual-write guarantee from the spec. **Do not "fix" the ordering.**

**Files:**
- Create: `src/services/Chat/Chat.Application/Chats/Errors/ChatOperationErrors.cs`
- Create: `src/services/Chat/Chat.Application/Chats/ModelUsability.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Results/TurnStartedResult.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatCommand.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatCommandValidator.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatHandler.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/SendMessage/SendMessageCommand.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/SendMessage/SendMessageCommandValidator.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/SendMessage/SendMessageHandler.cs`
- Create: `tests/Chat/Chat.Application.Tests/Turns/FakeChatRepository.cs`
- Create: `tests/Chat/Chat.Application.Tests/Turns/FakeMessageBus.cs`
- Create: `tests/Chat/Chat.Application.Tests/Turns/FakeUnitOfWork.cs`
- Test: `tests/Chat/Chat.Application.Tests/Turns/CreateChatHandlerTests.cs`
- Test: `tests/Chat/Chat.Application.Tests/Turns/SendMessageHandlerTests.cs`

- [ ] **Step 1: Create the fakes**

`FakeChatRepository.cs`:

```csharp
using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Application.Tests.Turns;

internal sealed class FakeChatRepository : IChatRepository
{
    private readonly List<ChatThread> _threads = [];

    public IReadOnlyList<ChatThread> Threads => _threads;

    public void Seed(ChatThread thread) => _threads.Add(thread);

    public Task<ChatThread?> GetByIdAsync(ChatId id, UserId userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_threads.FirstOrDefault(thread => thread.Id == id && thread.UserId == userId));

    public void Add(ChatThread chat) => _threads.Add(chat);
}
```

`FakeMessageBus.cs`:

```csharp
using Shared.Application.Messaging;

namespace Chat.Application.Tests.Turns;

internal sealed class FakeMessageBus : IMessageBus
{
    public List<object> Published { get; } = [];

    public Task PublishAsync<T>(T integrationEvent, CancellationToken cancellationToken = default)
        where T : class
    {
        Published.Add(integrationEvent);
        return Task.CompletedTask;
    }

    public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken = default)
    {
        Published.Add(integrationEvent);
        return Task.CompletedTask;
    }
}
```

`FakeUnitOfWork.cs` — open `src/services/Chat/Chat.Application/Abstractions/Database/IUnitOfWork.cs` first and match its exact signature (it mirrors `DbContext.SaveChangesAsync`):

```csharp
using Chat.Application.Abstractions.Database;

namespace Chat.Application.Tests.Turns;

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.FromResult(0);
    }
}
```

- [ ] **Step 2: Write the failing handler tests**

`CreateChatHandlerTests.cs`:

```csharp
using Chat.Application.Chats.Commands.CreateChat;
using Chat.Application.Chats.Results;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Tests.ModelCatalog;
using Chat.Application.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;

using ErrorOr;

namespace Chat.Application.Tests.Turns;

public sealed class CreateChatHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeChatRepository _chats = new();
    private readonly FakeLlmProviderRepository _providers = new();
    private readonly FakeMessageBus _messageBus = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

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

    private CreateChatHandler CreateHandler() => new
    (
        userContext: new FakeUserContext("auth0|user-1"),
        chats: _chats,
        llmProviders: _providers,
        messageBus: _messageBus,
        unitOfWork: _unitOfWork,
        dateTimeProvider: new FakeDateTimeProvider(Now)
    );

    [Fact]
    public async Task Handle_PersistsThreadWithGeneratingAssistant_AndPublishesTurnRequested()
    {
        LlmModel model = SeedModel();

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new CreateChatCommand("What is Redis?", model.Id.Value), CancellationToken.None);

        Assert.False(result.IsError);

        ChatThread thread = Assert.Single(_chats.Threads);
        Assert.Equal(2, thread.Messages.Count);
        Assert.Contains(thread.Messages, m => m.Status == MessageStatus.Generating);

        TurnRequested job = Assert.IsType<TurnRequested>(Assert.Single(_messageBus.Published));
        Assert.Equal(result.Value.AssistantMessageId, job.AssistantMessageId);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Handle_WhenModelUnknown_ReturnsLlmModelNotFound()
    {
        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new CreateChatCommand("Hello", Guid.CreateVersion7()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.LlmModelNotFound", result.FirstError.Code);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }
}
```

`SendMessageHandlerTests.cs`:

```csharp
using Chat.Application.Chats.Commands.SendMessage;
using Chat.Application.Chats.Results;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Tests.ModelCatalog;
using Chat.Application.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Turns;

public sealed class SendMessageHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeChatRepository _chats = new();
    private readonly FakeLlmProviderRepository _providers = new();
    private readonly FakeMessageBus _messageBus = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

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

    private ChatThread SeedThreadWithCompletedTurn(LlmModel model)
    {
        ChatThread thread = ChatThread.Create
        (
            userId: UserId.Create("auth0|user-1").Value,
            title: ChatTitle.Create("Hello").Value,
            firstUserMessage: MessageContent.Create("Hello").Value,
            createdAt: Now
        );

        ChatMessage assistant = thread.BeginAssistantMessage(thread.CurrentMessageId, model.Id, Now).Value;
        thread.CompleteAssistantMessage(assistant.Id, MessageContent.Create("Hi there!").Value, Now);

        _chats.Seed(thread);
        return thread;
    }

    private SendMessageHandler CreateHandler() => new
    (
        userContext: new FakeUserContext("auth0|user-1"),
        chats: _chats,
        llmProviders: _providers,
        messageBus: _messageBus,
        unitOfWork: _unitOfWork,
        dateTimeProvider: new FakeDateTimeProvider(Now)
    );

    [Fact]
    public async Task Handle_AppendsUserAndGeneratingAssistant_AndPublishesTurnRequested()
    {
        LlmModel model = SeedModel();
        ChatThread thread = SeedThreadWithCompletedTurn(model);

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new SendMessageCommand(thread.Id.Value, "Tell me more", model.Id.Value), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(4, thread.Messages.Count);

        TurnRequested job = Assert.IsType<TurnRequested>(Assert.Single(_messageBus.Published));
        Assert.Equal(result.Value.AssistantMessageId, job.AssistantMessageId);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Handle_WhileAssistantIsStillGenerating_ReturnsParentStillGenerating()
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
            .Handle(new SendMessageCommand(thread.Id.Value, "Too eager", model.Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.ParentStillGenerating", result.FirstError.Code);
        Assert.Empty(_messageBus.Published);
    }

    [Fact]
    public async Task Handle_WhenChatUnknown_ReturnsChatNotFound()
    {
        LlmModel model = SeedModel();

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new SendMessageCommand(Guid.CreateVersion7(), "Hello", model.Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.NotFound", result.FirstError.Code);
    }
}
```

- [ ] **Step 3: Run to verify compile failure**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~CreateChatHandlerTests|FullyQualifiedName~SendMessageHandlerTests"`
Expected: build error — command types not found.

- [ ] **Step 4: Create `ChatOperationErrors.cs`**

```csharp
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

namespace Chat.Application.Chats.Errors;

public static class ChatOperationErrors
{
    public static Error ChatNotFound(ChatId chatId) =>
        Error.NotFound
        (
            code: "Chat.NotFound",
            description: $"No chat found with id '{chatId.Value}'."
        );

    public static Error LlmModelNotFound(LlmModelId modelId) =>
        Error.NotFound
        (
            code: "Chat.LlmModelNotFound",
            description: $"No enabled LLM model found with id '{modelId.Value}'."
        );

    public static Error LlmModelDisabled(LlmModelId modelId) =>
        Error.Conflict
        (
            code: "Chat.LlmModelDisabled",
            description: $"LLM model '{modelId.Value}' is disabled."
        );
}
```

- [ ] **Step 5: Create `ModelUsability.cs`**

```csharp
using Chat.Application.Chats.Errors;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

namespace Chat.Application.Chats;

internal static class ModelUsability
{
    public static async Task<ErrorOr<Success>> EnsureUsableAsync
    (
        ILlmProviderRepository llmProviders,
        LlmModelId modelId,
        CancellationToken cancellationToken
    )
    {
        LlmProvider? provider = await llmProviders.GetByModelIdAsync(modelId, cancellationToken);
        LlmModel? model = provider?.FindModel(modelId);

        if (provider is null || model is null)
        {
            return ChatOperationErrors.LlmModelNotFound(modelId);
        }

        if (!provider.IsEnabled || !model.IsEnabled)
        {
            return ChatOperationErrors.LlmModelDisabled(modelId);
        }

        return Result.Success;
    }
}
```

- [ ] **Step 6: Create `TurnStartedResult.cs`**

```csharp
namespace Chat.Application.Chats.Results;

public sealed record TurnStartedResult
(
    Guid ChatId,
    Guid UserMessageId,
    Guid AssistantMessageId
);
```

- [ ] **Step 7: Create the CreateChat command, validator, handler**

`CreateChatCommand.cs`:

```csharp
using Chat.Application.Chats.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.CreateChat;

public sealed record CreateChatCommand(string Message, Guid LlmModelId) : ICommand<ErrorOr<TurnStartedResult>>;
```

`CreateChatCommandValidator.cs`:

```csharp
using Chat.Domain.Chats.ValueObjects;

using FluentValidation;

namespace Chat.Application.Chats.Commands.CreateChat;

internal sealed class CreateChatCommandValidator : AbstractValidator<CreateChatCommand>
{
    public CreateChatCommandValidator()
    {
        RuleFor(x => x.Message)
            .NotEmpty()
            .MaximumLength(MessageContent.MaxLength);

        RuleFor(x => x.LlmModelId)
            .NotEmpty();
    }
}
```

`CreateChatHandler.cs`:

```csharp
using Chat.Application.Abstractions.Database;
using Chat.Application.Chats.Results;
using Chat.Application.Turns;
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

namespace Chat.Application.Chats.Commands.CreateChat;

internal sealed class CreateChatHandler(
    IUserContext userContext,
    IChatRepository chats,
    ILlmProviderRepository llmProviders,
    IMessageBus messageBus,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<CreateChatCommand, ErrorOr<TurnStartedResult>>
{
    public async ValueTask<ErrorOr<TurnStartedResult>> Handle(CreateChatCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userId = UserId.Create(userContext.UserId);
        ErrorOr<MessageContent> content = MessageContent.Create(command.Message);
        ErrorOr<LlmModelId> modelId = LlmModelId.Create(command.LlmModelId);

        List<Error> errors = [];

        if (userId.IsError)
        {
            errors.AddRange(userId.Errors);
        }

        if (content.IsError)
        {
            errors.AddRange(content.Errors);
        }

        if (modelId.IsError)
        {
            errors.AddRange(modelId.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        ErrorOr<Success> modelUsable = await ModelUsability.EnsureUsableAsync(llmProviders, modelId.Value, cancellationToken);

        if (modelUsable.IsError)
        {
            return modelUsable.Errors;
        }

        string trimmedMessage = command.Message.Trim();
        string titleSource = trimmedMessage.Length <= ChatTitle.MaxLength
            ? trimmedMessage
            : trimmedMessage[..ChatTitle.MaxLength];

        ErrorOr<ChatTitle> title = ChatTitle.Create(titleSource);

        if (title.IsError)
        {
            return title.Errors;
        }

        DateTimeOffset now = dateTimeProvider.UtcNow;

        ChatThread thread = ChatThread.Create
        (
            userId: userId.Value,
            title: title.Value,
            firstUserMessage: content.Value,
            createdAt: now
        );

        ChatMessageId userMessageId = thread.CurrentMessageId;

        ErrorOr<ChatMessage> assistantMessage = thread.BeginAssistantMessage
        (
            parentMessageId: userMessageId,
            llmModelId: modelId.Value,
            createdAt: now
        );

        if (assistantMessage.IsError)
        {
            return assistantMessage.Errors;
        }

        chats.Add(thread);

        // Published BEFORE SaveChangesAsync on purpose: the MassTransit bus outbox buffers
        // this and writes it to the outbox table inside the same transaction (spec: no dual-write).
        await messageBus.PublishAsync
        (
            new TurnRequested(thread.Id.Value, userId.Value.Value, assistantMessage.Value.Id.Value),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new TurnStartedResult
        (
            ChatId: thread.Id.Value,
            UserMessageId: userMessageId.Value,
            AssistantMessageId: assistantMessage.Value.Id.Value
        );
    }
}
```

- [ ] **Step 8: Create the SendMessage command, validator, handler**

`SendMessageCommand.cs`:

```csharp
using Chat.Application.Chats.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.SendMessage;

public sealed record SendMessageCommand(Guid ChatId, string Message, Guid LlmModelId) : ICommand<ErrorOr<TurnStartedResult>>;
```

`SendMessageCommandValidator.cs`:

```csharp
using Chat.Domain.Chats.ValueObjects;

using FluentValidation;

namespace Chat.Application.Chats.Commands.SendMessage;

internal sealed class SendMessageCommandValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageCommandValidator()
    {
        RuleFor(x => x.ChatId)
            .NotEmpty();

        RuleFor(x => x.Message)
            .NotEmpty()
            .MaximumLength(MessageContent.MaxLength);

        RuleFor(x => x.LlmModelId)
            .NotEmpty();
    }
}
```

`SendMessageHandler.cs`:

```csharp
using Chat.Application.Abstractions.Database;
using Chat.Application.Chats.Errors;
using Chat.Application.Chats.Results;
using Chat.Application.Turns;
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

namespace Chat.Application.Chats.Commands.SendMessage;

internal sealed class SendMessageHandler(
    IUserContext userContext,
    IChatRepository chats,
    ILlmProviderRepository llmProviders,
    IMessageBus messageBus,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<SendMessageCommand, ErrorOr<TurnStartedResult>>
{
    public async ValueTask<ErrorOr<TurnStartedResult>> Handle(SendMessageCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userId = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatId = ChatId.Create(command.ChatId);
        ErrorOr<MessageContent> content = MessageContent.Create(command.Message);
        ErrorOr<LlmModelId> modelId = LlmModelId.Create(command.LlmModelId);

        List<Error> errors = [];

        if (userId.IsError)
        {
            errors.AddRange(userId.Errors);
        }

        if (chatId.IsError)
        {
            errors.AddRange(chatId.Errors);
        }

        if (content.IsError)
        {
            errors.AddRange(content.Errors);
        }

        if (modelId.IsError)
        {
            errors.AddRange(modelId.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        ErrorOr<Success> modelUsable = await ModelUsability.EnsureUsableAsync(llmProviders, modelId.Value, cancellationToken);

        if (modelUsable.IsError)
        {
            return modelUsable.Errors;
        }

        ChatThread? thread = await chats.GetByIdAsync(chatId.Value, userId.Value, cancellationToken);

        if (thread is null)
        {
            return ChatOperationErrors.ChatNotFound(chatId.Value);
        }

        DateTimeOffset now = dateTimeProvider.UtcNow;

        ErrorOr<ChatMessage> userMessage = thread.AddUserMessage
        (
            parentMessageId: thread.CurrentMessageId,
            content: content.Value,
            createdAt: now
        );

        if (userMessage.IsError)
        {
            return userMessage.Errors;
        }

        ErrorOr<ChatMessage> assistantMessage = thread.BeginAssistantMessage
        (
            parentMessageId: userMessage.Value.Id,
            llmModelId: modelId.Value,
            createdAt: now
        );

        if (assistantMessage.IsError)
        {
            return assistantMessage.Errors;
        }

        // Published BEFORE SaveChangesAsync on purpose: the MassTransit bus outbox buffers
        // this and writes it to the outbox table inside the same transaction (spec: no dual-write).
        await messageBus.PublishAsync
        (
            new TurnRequested(thread.Id.Value, userId.Value.Value, assistantMessage.Value.Id.Value),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new TurnStartedResult
        (
            ChatId: thread.Id.Value,
            UserMessageId: userMessage.Value.Id.Value,
            AssistantMessageId: assistantMessage.Value.Id.Value
        );
    }
}
```

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~CreateChatHandlerTests|FullyQualifiedName~SendMessageHandlerTests"`
Expected: PASS (5 tests).

- [ ] **Step 10: Commit**

```bash
git add src/services/Chat/Chat.Application tests/Chat/Chat.Application.Tests
git commit -m "feat(chat): add create-chat and send-message commands with outboxed turn jobs"
```

---

## Task 5: ChatTurnOrchestrator

The whole worker-side sequence. **Rule 2: this class is sequencing only.** If you are tempted to add an `if` that isn't error/idempotency plumbing, you need a new seam instead.

Error-handling contract (from the spec — do not deviate):
- Malformed job / missing thread / missing message → log + return (ack; poison-proof).
- Assistant message no longer `Generating` → return (idempotent redelivery).
- Exceptions while **loading or building context** → propagate (MassTransit retries, then `_error` queue).
- Exceptions from the **agent run** → `FailAssistantMessage` + `FailedEvent` + ack (never blind-retry a half-streamed turn).
- `OperationCanceledException` → rethrow (shutdown; redelivery restarts the turn; `ResetAsync` clears the partial stream).

**Files:**
- Create: `src/services/Chat/Chat.Application/Turns/ChatTurnOrchestrator.cs`
- Modify: `src/services/Chat/Chat.Application/Chat.Application.csproj` (add logging abstractions)
- Create: `tests/Chat/Chat.Application.Tests/Turns/FakeAgentRunner.cs`
- Create: `tests/Chat/Chat.Application.Tests/Turns/RecordingTokenPublisher.cs`
- Create: `tests/Chat/Chat.Application.Tests/Turns/FakeContextBuilder.cs`
- Test: `tests/Chat/Chat.Application.Tests/Turns/ChatTurnOrchestratorTests.cs`

- [ ] **Step 1: Add the logging package to `Chat.Application.csproj`** (version comes from central package management):

```xml
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
```

- [ ] **Step 2: Create the fakes**

`FakeAgentRunner.cs`:

```csharp
using Chat.Application.Abstractions.Turns;
using Chat.Application.Turns;

namespace Chat.Application.Tests.Turns;

internal sealed class FakeAgentRunner(Func<TurnContext, IAsyncEnumerable<TurnEvent>> script) : IAgentRunner
{
    public IAsyncEnumerable<TurnEvent> RunAsync(TurnContext context, CancellationToken cancellationToken) =>
        script(context);

    public static async IAsyncEnumerable<TurnEvent> Tokens(Guid turnId, params string[] tokens)
    {
        foreach (string token in tokens)
        {
            await Task.Yield();
            yield return new TokenEvent(turnId, token);
        }
    }

    public static async IAsyncEnumerable<TurnEvent> TokenThenThrow(Guid turnId, string token, Exception exception)
    {
        await Task.Yield();
        yield return new TokenEvent(turnId, token);
        throw exception;
    }
}
```

`RecordingTokenPublisher.cs`:

```csharp
using Chat.Application.Abstractions.Turns;
using Chat.Application.Turns;

namespace Chat.Application.Tests.Turns;

internal sealed class RecordingTokenPublisher : ITokenPublisher
{
    public List<TurnEvent> Events { get; } = [];

    public int ResetCount { get; private set; }

    public Task PublishAsync(TurnEvent turnEvent, CancellationToken cancellationToken)
    {
        Events.Add(turnEvent);
        return Task.CompletedTask;
    }

    public Task ResetAsync(Guid turnId, CancellationToken cancellationToken)
    {
        ResetCount++;
        return Task.CompletedTask;
    }
}
```

`FakeContextBuilder.cs`:

```csharp
using Chat.Application.Abstractions.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;

using ErrorOr;

namespace Chat.Application.Tests.Turns;

internal sealed class FakeContextBuilder : IContextBuilder
{
    public Task<ErrorOr<TurnContext>> BuildAsync
    (
        ChatThread thread,
        ChatMessage assistantMessage,
        RetrievedMemories memories,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult<ErrorOr<TurnContext>>(new TurnContext
        (
            TurnId: assistantMessage.Id.Value,
            ChatId: thread.Id.Value,
            UserId: thread.UserId.Value,
            ExternalModelId: "gpt-4.1",
            SystemPrompt: "test",
            Messages: [new TurnMessage(TurnRole.User, "Hello")]
        ));
}
```

- [ ] **Step 3: Write the failing orchestrator tests**

```csharp
using Chat.Application.Abstractions.Turns;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using Microsoft.Extensions.Logging.Abstractions;

namespace Chat.Application.Tests.Turns;

public sealed class ChatTurnOrchestratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeChatRepository _chats = new();
    private readonly RecordingTokenPublisher _publisher = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private (ChatThread Thread, ChatMessage Assistant, TurnRequested Job) SeedPendingTurn()
    {
        ChatThread thread = ChatThread.Create
        (
            userId: UserId.Create("auth0|user-1").Value,
            title: ChatTitle.Create("Hello").Value,
            firstUserMessage: MessageContent.Create("Hello").Value,
            createdAt: Now
        );

        ChatMessage assistant = thread.BeginAssistantMessage(thread.CurrentMessageId, null, Now).Value;
        _chats.Seed(thread);

        return (thread, assistant, new TurnRequested(thread.Id.Value, "auth0|user-1", assistant.Id.Value));
    }

    private ChatTurnOrchestrator CreateOrchestrator(IAgentRunner runner) => new
    (
        chats: _chats,
        memoryRetriever: new NoOpMemoryRetriever(),
        contextBuilder: new FakeContextBuilder(),
        agentRunner: runner,
        publisher: _publisher,
        unitOfWork: _unitOfWork,
        dateTimeProvider: new FakeDateTimeProvider(Now),
        logger: NullLogger<ChatTurnOrchestrator>.Instance
    );

    [Fact]
    public async Task RunTurnAsync_HappyPath_CompletesMessageAndPublishesDoneLast()
    {
        (ChatThread thread, ChatMessage assistant, TurnRequested job) = SeedPendingTurn();

        FakeAgentRunner runner = new(ctx => FakeAgentRunner.Tokens(ctx.TurnId, "Hello", " world"));

        await CreateOrchestrator(runner).RunTurnAsync(job, CancellationToken.None);

        Assert.Equal(MessageStatus.Completed, assistant.Status);
        Assert.Equal("Hello world", assistant.Content!.Value);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Equal(1, _publisher.ResetCount);
        Assert.IsType<DoneEvent>(_publisher.Events[^1]);
        Assert.Equal(2, _publisher.Events.OfType<TokenEvent>().Count());
    }

    [Fact]
    public async Task RunTurnAsync_WhenAgentThrows_FailsMessageAndPublishesFailedEvent()
    {
        (ChatThread thread, ChatMessage assistant, TurnRequested job) = SeedPendingTurn();

        FakeAgentRunner runner = new(ctx =>
            FakeAgentRunner.TokenThenThrow(ctx.TurnId, "partial", new InvalidOperationException("provider down")));

        await CreateOrchestrator(runner).RunTurnAsync(job, CancellationToken.None);

        Assert.Equal(MessageStatus.Failed, assistant.Status);
        Assert.Contains("provider down", assistant.FailureReason!.Value);
        Assert.Equal(1, _unitOfWork.SaveCount);
        FailedEvent failed = Assert.IsType<FailedEvent>(_publisher.Events[^1]);
        Assert.Equal(assistant.Id.Value, failed.TurnId);
    }

    [Fact]
    public async Task RunTurnAsync_WhenMessageAlreadyTerminal_DoesNothing()
    {
        (ChatThread thread, ChatMessage assistant, TurnRequested job) = SeedPendingTurn();
        thread.CompleteAssistantMessage(assistant.Id, MessageContent.Create("done already").Value, Now);

        FakeAgentRunner runner = new(ctx => FakeAgentRunner.Tokens(ctx.TurnId, "should not run"));

        await CreateOrchestrator(runner).RunTurnAsync(job, CancellationToken.None);

        Assert.Empty(_publisher.Events);
        Assert.Equal(0, _publisher.ResetCount);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task RunTurnAsync_WhenAgentReturnsNoText_FailsTheTurn()
    {
        (ChatThread thread, ChatMessage assistant, TurnRequested job) = SeedPendingTurn();

        FakeAgentRunner runner = new(ctx => FakeAgentRunner.Tokens(ctx.TurnId));

        await CreateOrchestrator(runner).RunTurnAsync(job, CancellationToken.None);

        Assert.Equal(MessageStatus.Failed, assistant.Status);
        Assert.IsType<FailedEvent>(_publisher.Events[^1]);
    }
}
```

- [ ] **Step 4: Run to verify compile failure**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~ChatTurnOrchestratorTests"`
Expected: build error — `ChatTurnOrchestrator` not found.

- [ ] **Step 5: Create `ChatTurnOrchestrator.cs`**

```csharp
using System.Text;

using Chat.Application.Abstractions.Database;
using Chat.Application.Abstractions.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Microsoft.Extensions.Logging;

using SharedKernel;

namespace Chat.Application.Turns;

/// <summary>
/// Pure sequencing of one chat turn (spec Rule 2). Everything interesting lives behind a seam;
/// adding a pipeline step here means introducing a new interface, never inline business logic.
/// </summary>
public sealed partial class ChatTurnOrchestrator(
    IChatRepository chats,
    IMemoryRetriever memoryRetriever,
    IContextBuilder contextBuilder,
    IAgentRunner agentRunner,
    ITokenPublisher publisher,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    ILogger<ChatTurnOrchestrator> logger)
{
    public async Task RunTurnAsync(TurnRequested job, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userId = UserId.Create(job.UserId);
        ErrorOr<ChatId> chatId = ChatId.Create(job.ChatId);
        ErrorOr<ChatMessageId> messageId = ChatMessageId.Create(job.AssistantMessageId);

        if (userId.IsError || chatId.IsError || messageId.IsError)
        {
            LogMalformedJob(job.ChatId, job.AssistantMessageId);
            return;
        }

        ChatThread? thread = await chats.GetByIdAsync(chatId.Value, userId.Value, cancellationToken);
        ChatMessage? assistantMessage = thread?.FindMessage(messageId.Value);

        if (thread is null || assistantMessage is null)
        {
            LogTurnTargetMissing(job.ChatId, job.AssistantMessageId);
            return;
        }

        if (assistantMessage.Status != MessageStatus.Generating)
        {
            // Redelivery after a finished run — idempotent no-op.
            LogTurnAlreadyTerminal(job.AssistantMessageId);
            return;
        }

        // A crashed previous attempt may have left a partial stream behind; start clean
        // so a resubscribed client never sees duplicated tokens.
        await publisher.ResetAsync(job.AssistantMessageId, cancellationToken);

        RetrievedMemories memories = await memoryRetriever.RetrieveAsync(job, cancellationToken);

        ErrorOr<TurnContext> context = await contextBuilder.BuildAsync(thread, assistantMessage, memories, cancellationToken);

        if (context.IsError)
        {
            await FailTurnAsync(thread, assistantMessage, context.FirstError.Description, cancellationToken);
            return;
        }

        StringBuilder text = new();

        try
        {
            await foreach (TurnEvent turnEvent in agentRunner.RunAsync(context.Value, cancellationToken))
            {
                if (turnEvent is TokenEvent token)
                {
                    text.Append(token.Text);
                }

                await publisher.PublishAsync(turnEvent, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown mid-turn: leave the message Generating; redelivery restarts the turn.
            throw;
        }
        catch (Exception exception)
        {
            LogAgentRunFailed(exception, job.AssistantMessageId);
            await FailTurnAsync(thread, assistantMessage, exception.Message, cancellationToken);
            return;
        }

        ErrorOr<MessageContent> content = MessageContent.Create(text.ToString());

        if (content.IsError)
        {
            await FailTurnAsync(thread, assistantMessage, "The model returned an empty response.", cancellationToken);
            return;
        }

        ErrorOr<ChatMessage> completion = thread.CompleteAssistantMessage
        (
            messageId: assistantMessage.Id,
            content: content.Value,
            completedAt: dateTimeProvider.UtcNow
        );

        if (completion.IsError)
        {
            LogTurnAlreadyTerminal(job.AssistantMessageId);
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await publisher.PublishAsync(new DoneEvent(job.AssistantMessageId), cancellationToken);
    }

    private async Task FailTurnAsync
    (
        ChatThread thread,
        ChatMessage assistantMessage,
        string reason,
        CancellationToken cancellationToken
    )
    {
        string truncated = reason.Length <= FailureReason.MaxLength ? reason : reason[..FailureReason.MaxLength];
        ErrorOr<FailureReason> failureReason = FailureReason.Create(truncated);

        ErrorOr<ChatMessage> failure = thread.FailAssistantMessage
        (
            messageId: assistantMessage.Id,
            reason: failureReason.IsError ? FailureReason.Create("The turn failed.").Value : failureReason.Value,
            failedAt: dateTimeProvider.UtcNow
        );

        if (failure.IsError)
        {
            LogTurnAlreadyTerminal(assistantMessage.Id.Value);
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await publisher.PublishAsync(new FailedEvent(assistantMessage.Id.Value, truncated), cancellationToken);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Discarded malformed turn job for chat {ChatId}, message {AssistantMessageId}")]
    private partial void LogMalformedJob(Guid chatId, Guid assistantMessageId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Turn target not found for chat {ChatId}, message {AssistantMessageId}")]
    private partial void LogTurnTargetMissing(Guid chatId, Guid assistantMessageId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information,
        Message = "Turn {AssistantMessageId} is already terminal; skipping (idempotent redelivery)")]
    private partial void LogTurnAlreadyTerminal(Guid assistantMessageId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error,
        Message = "Agent run failed for turn {AssistantMessageId}")]
    private partial void LogAgentRunFailed(Exception exception, Guid assistantMessageId);
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~ChatTurnOrchestratorTests"`
Expected: PASS (4 tests).

- [ ] **Step 7: Commit**

```bash
git add src/services/Chat/Chat.Application tests/Chat/Chat.Application.Tests
git commit -m "feat(chat): add chat turn orchestrator with fail/idempotency semantics"
```

---

## Task 6: Redis Stream publisher and reader

Per-turn Redis Stream `chat:turn:{assistantMessageId}` — XADD on publish, TTL after done/failed, replay-from-offset for SSE. No unit tests (thin Redis adapters, verified end-to-end in Task 12).

**Files:**
- Modify: `src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj` (StackExchange.Redis)
- Create: `src/services/Chat/Chat.Infrastructure/Turns/RedisStreamTokenPublisher.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Turns/RedisTurnStreamReader.cs`

- [ ] **Step 1: Add the StackExchange.Redis package** (CPM records the version in `Directory.Packages.props` automatically):

Run: `dotnet add src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj package StackExchange.Redis`

- [ ] **Step 2: Create `RedisStreamTokenPublisher.cs`**

```csharp
using Chat.Application.Abstractions.Turns;
using Chat.Application.Turns;

using StackExchange.Redis;

namespace Chat.Infrastructure.Turns;

internal sealed class RedisStreamTokenPublisher(IConnectionMultiplexer redis) : ITokenPublisher
{
    private const string KeyPrefix = "chat:turn:";

    private static readonly TimeSpan CompletedStreamTtl = TimeSpan.FromMinutes(10);

    internal static string StreamKey(Guid turnId) => $"{KeyPrefix}{turnId}";

    public async Task PublishAsync(TurnEvent turnEvent, CancellationToken cancellationToken)
    {
        IDatabase db = redis.GetDatabase();
        string key = StreamKey(turnEvent.TurnId);

        await db.StreamAddAsync(key, [new NameValueEntry("data", TurnEventSerializer.Serialize(turnEvent))]);

        if (turnEvent is DoneEvent or FailedEvent)
        {
            await db.KeyExpireAsync(key, CompletedStreamTtl);
        }
    }

    public async Task ResetAsync(Guid turnId, CancellationToken cancellationToken)
    {
        await redis.GetDatabase().KeyDeleteAsync(StreamKey(turnId));
    }
}
```

- [ ] **Step 3: Create `RedisTurnStreamReader.cs`**

```csharp
using System.Runtime.CompilerServices;

using Chat.Application.Abstractions.Turns;
using Chat.Application.Turns;

using StackExchange.Redis;

namespace Chat.Infrastructure.Turns;

internal sealed class RedisTurnStreamReader(IConnectionMultiplexer redis) : ITurnStreamReader
{
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(150);

    public async IAsyncEnumerable<TurnStreamEntry> ReadAsync
    (
        Guid turnId,
        string? fromEntryId,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        IDatabase db = redis.GetDatabase();
        string key = RedisStreamTokenPublisher.StreamKey(turnId);
        RedisValue position = fromEntryId ?? "0-0";

        while (!cancellationToken.IsCancellationRequested)
        {
            StreamEntry[] entries = await db.StreamReadAsync(key, position, count: 128);

            foreach (StreamEntry entry in entries)
            {
                position = entry.Id;

                string? json = entry["data"];

                if (json is null)
                {
                    continue;
                }

                TurnEvent? turnEvent = TurnEventSerializer.Deserialize(json);

                if (turnEvent is null)
                {
                    continue;
                }

                yield return new TurnStreamEntry(entry.Id.ToString()!, turnEvent);

                if (turnEvent is DoneEvent or FailedEvent)
                {
                    yield break;
                }
            }

            if (entries.Length == 0)
            {
                await Task.Delay(PollDelay, cancellationToken);
            }
        }
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/services/Chat/Chat.Infrastructure`
Expected: success, no warnings about Redis types.

- [ ] **Step 5: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure Directory.Packages.props
git commit -m "feat(chat): add per-turn redis stream publisher and replayable reader"
```

---

## Task 7: Agent Framework runner (the quarantine zone)

**Rule 1: `Microsoft.Agents.AI` / `Microsoft.Extensions.AI` / `OpenAI` types are allowed in these three files and NOWHERE else.** Note the `AIChatMessage` alias — the framework's `ChatMessage` collides with our domain's `ChatMessage`; the alias keeps that collision contained here.

Also creates the `Chat.Infrastructure.Tests` project for the mapper test.

**Files:**
- Modify: `src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj`
- Create: `src/services/Chat/Chat.Infrastructure/Agents/AgentOptions.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Agents/TurnEventMapper.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Agents/AgentFrameworkRunner.cs`
- Create: `tests/Chat/Chat.Infrastructure.Tests/Chat.Infrastructure.Tests.csproj`
- Create: `tests/Chat/Chat.Infrastructure.Tests/GlobalUsings.cs`
- Test: `tests/Chat/Chat.Infrastructure.Tests/Agents/TurnEventMapperTests.cs`

- [ ] **Step 1: Add the Agent Framework packages** (prerelease; CPM records versions centrally):

```bash
dotnet add src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj package Microsoft.Agents.AI --prerelease
dotnet add src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj package Microsoft.Agents.AI.OpenAI --prerelease
```

- [ ] **Step 2: Create the test project**

`tests/Chat/Chat.Infrastructure.Tests/Chat.Infrastructure.Tests.csproj` (mirrors `Chat.Application.Tests.csproj`):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <NoWarn>$(NoWarn);CA1822</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\src\services\Chat\Chat.Infrastructure\Chat.Infrastructure.csproj" />
  </ItemGroup>

</Project>
```

`GlobalUsings.cs`:

```csharp
global using Xunit;
```

Add to the solution and grant internals access. First check how `Chat.Application` exposes internals to its test project (look for `InternalsVisibleTo` in `Chat.Application.csproj` or a shared props file) and copy that mechanism. If it's csproj-based, add to `Chat.Infrastructure.csproj`:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="Chat.Infrastructure.Tests" />
  </ItemGroup>
```

Run: `dotnet sln Nova.slnx add tests/Chat/Chat.Infrastructure.Tests/Chat.Infrastructure.Tests.csproj`

- [ ] **Step 3: Write the failing mapper test**

> Note: if `AgentRunResponseUpdate` cannot be constructed with an object initializer in the installed package version, use whatever public constructor it exposes (e.g. `new AgentRunResponseUpdate(ChatRole.Assistant, [.. contents])`) — the assertion targets are what matter.

```csharp
using Chat.Application.Turns;
using Chat.Infrastructure.Agents;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Chat.Infrastructure.Tests.Agents;

public sealed class TurnEventMapperTests
{
    private static readonly Guid TurnId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Map_TextContent_YieldsTokenEvent()
    {
        AgentRunResponseUpdate update = new() { Contents = [new TextContent("Hello")] };

        TurnEvent single = Assert.Single(TurnEventMapper.Map(TurnId, "gpt-4.1", update));

        TokenEvent token = Assert.IsType<TokenEvent>(single);
        Assert.Equal("Hello", token.Text);
        Assert.Equal(TurnId, token.TurnId);
    }

    [Fact]
    public void Map_EmptyTextContent_YieldsNothing()
    {
        AgentRunResponseUpdate update = new() { Contents = [new TextContent("")] };

        Assert.Empty(TurnEventMapper.Map(TurnId, "gpt-4.1", update));
    }

    [Fact]
    public void Map_UsageContent_YieldsUsageEventWithFallbackModel()
    {
        UsageDetails details = new() { InputTokenCount = 120, OutputTokenCount = 45 };
        AgentRunResponseUpdate update = new() { Contents = [new UsageContent(details)] };

        TurnEvent single = Assert.Single(TurnEventMapper.Map(TurnId, "gpt-4.1", update));

        UsageEvent usage = Assert.IsType<UsageEvent>(single);
        Assert.Equal("gpt-4.1", usage.Model);
        Assert.Equal(120, usage.InputTokens);
        Assert.Equal(45, usage.OutputTokens);
    }

    [Fact]
    public void Map_FunctionCallContent_YieldsToolCallEvent()
    {
        AgentRunResponseUpdate update = new()
        {
            Contents = [new FunctionCallContent("call-1", "search", new Dictionary<string, object?> { ["q"] = "redis" })]
        };

        TurnEvent single = Assert.Single(TurnEventMapper.Map(TurnId, "gpt-4.1", update));

        ToolCallEvent toolCall = Assert.IsType<ToolCallEvent>(single);
        Assert.Equal("search", toolCall.Tool);
        Assert.Contains("redis", toolCall.ArgsJson);
    }
}
```

- [ ] **Step 4: Run to verify compile failure**

Run: `dotnet test tests/Chat/Chat.Infrastructure.Tests`
Expected: build error — `TurnEventMapper` not found.

- [ ] **Step 5: Create `AgentOptions.cs`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace Chat.Infrastructure.Agents;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    [Required]
    public string BaseUrl { get; init; } = "https://openrouter.ai/api/v1";

    [Required]
    public string ApiKey { get; init; } = string.Empty;
}
```

- [ ] **Step 6: Create `TurnEventMapper.cs`**

```csharp
using System.Text.Json;

using Chat.Application.Turns;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Chat.Infrastructure.Agents;

internal static class TurnEventMapper
{
    private const int ToolResultSummaryMaxLength = 512;

    public static IEnumerable<TurnEvent> Map(Guid turnId, string fallbackModelId, AgentRunResponseUpdate update)
    {
        foreach (AIContent content in update.Contents)
        {
            switch (content)
            {
                case TextContent text when !string.IsNullOrEmpty(text.Text):
                    yield return new TokenEvent(turnId, text.Text);
                    break;

                case FunctionCallContent call:
                    yield return new ToolCallEvent(turnId, call.Name, JsonSerializer.Serialize(call.Arguments));
                    break;

                case FunctionResultContent result:
                    yield return new ToolResultEvent(turnId, result.CallId, Truncate(result.Result?.ToString()));
                    break;

                case UsageContent usage:
                    yield return new UsageEvent
                    (
                        turnId,
                        update.ModelId ?? fallbackModelId,
                        (int)(usage.Details.InputTokenCount ?? 0),
                        (int)(usage.Details.OutputTokenCount ?? 0)
                    );
                    break;
            }
        }
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= ToolResultSummaryMaxLength ? value : value[..ToolResultSummaryMaxLength];
    }
}
```

- [ ] **Step 7: Create `AgentFrameworkRunner.cs`**

```csharp
using System.ClientModel;
using System.Runtime.CompilerServices;

using Chat.Application.Abstractions.Turns;
using Chat.Application.Turns;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

using OpenAI;

using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Chat.Infrastructure.Agents;

/// <summary>
/// THE quarantine boundary (spec Rule 1): the only place Agent Framework types may appear.
/// Everything downstream speaks TurnEvent.
/// </summary>
internal sealed class AgentFrameworkRunner : IAgentRunner
{
    private readonly OpenAIClient _client;

    public AgentFrameworkRunner(IOptions<AgentOptions> options)
    {
        AgentOptions value = options.Value;

        _client = new OpenAIClient
        (
            new ApiKeyCredential(value.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(value.BaseUrl) }
        );
    }

    public async IAsyncEnumerable<TurnEvent> RunAsync
    (
        TurnContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        AIAgent agent = _client
            .GetChatClient(context.ExternalModelId)
            .CreateAIAgent(instructions: context.SystemPrompt);

        List<AIChatMessage> messages = context.Messages
            .Select(message => new AIChatMessage
            (
                message.Role == TurnRole.User ? ChatRole.User : ChatRole.Assistant,
                message.Text
            ))
            .ToList();

        await foreach (AgentRunResponseUpdate update in agent.RunStreamingAsync(messages, cancellationToken: cancellationToken))
        {
            foreach (TurnEvent turnEvent in TurnEventMapper.Map(context.TurnId, context.ExternalModelId, update))
            {
                yield return turnEvent;
            }
        }
    }
}
```

- [ ] **Step 8: Run the mapper tests to verify they pass**

Run: `dotnet test tests/Chat/Chat.Infrastructure.Tests`
Expected: PASS (4 tests). If the Agent Framework preview API surface differs (constructor shapes, property names), adapt **only** files inside `Chat.Infrastructure/Agents/` and this test file — nothing outside the quarantine may change.

- [ ] **Step 9: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure tests/Chat/Chat.Infrastructure.Tests Directory.Packages.props Nova.slnx
git commit -m "feat(chat): add quarantined agent framework runner and event mapper"
```

---

## Task 8: Telemetry decorator and PostHog adapter

**Rule 3 in action.** `TelemetryAgentRunner` lives in Chat.Application (it depends only on our own seams). The PostHog SDK is quarantined inside `PostHogAnalytics` exactly like the Agent Framework is inside `AgentFrameworkRunner`.

**Files:**
- Create: `src/services/Chat/Chat.Application/Turns/TelemetryAgentRunner.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Analytics/NullAnalytics.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Analytics/PostHogAnalytics.cs`
- Create: `tests/Chat/Chat.Application.Tests/Turns/FakeAnalytics.cs`
- Test: `tests/Chat/Chat.Application.Tests/Turns/TelemetryAgentRunnerTests.cs`

- [ ] **Step 1: Create `FakeAnalytics.cs`**

```csharp
using Chat.Application.Abstractions.Analytics;

namespace Chat.Application.Tests.Turns;

internal sealed class FakeAnalytics : IAnalytics
{
    public List<(string DistinctId, string EventName, Dictionary<string, object> Properties)> Captured { get; } = [];

    public void Capture(string distinctId, string eventName, Dictionary<string, object> properties) =>
        Captured.Add((distinctId, eventName, properties));
}
```

- [ ] **Step 2: Write the failing decorator test**

```csharp
using Chat.Application.Abstractions.Turns;
using Chat.Application.Turns;

namespace Chat.Application.Tests.Turns;

public sealed class TelemetryAgentRunnerTests
{
    private static readonly Guid TurnId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly TurnContext Context = new
    (
        TurnId: TurnId,
        ChatId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
        UserId: "auth0|user-1",
        ExternalModelId: "gpt-4.1",
        SystemPrompt: "test",
        Messages: [new TurnMessage(TurnRole.User, "Hello")]
    );

    private static async IAsyncEnumerable<TurnEvent> Script()
    {
        await Task.Yield();
        yield return new TokenEvent(TurnId, "Hello");
        yield return new ToolCallEvent(TurnId, "search", "{}");
        yield return new UsageEvent(TurnId, "gpt-4.1-mini", 120, 45);
    }

    [Fact]
    public async Task RunAsync_PassesEveryEventThroughUnchanged_ThenCapturesGenerationAndToolEvents()
    {
        FakeAnalytics analytics = new();
        FakeAgentRunner inner = new(_ => Script());

        TelemetryAgentRunner decorator = new(inner, analytics);

        List<TurnEvent> seen = [];

        await foreach (TurnEvent turnEvent in decorator.RunAsync(Context, CancellationToken.None))
        {
            seen.Add(turnEvent);
        }

        Assert.Equal(3, seen.Count);
        Assert.IsType<TokenEvent>(seen[0]);
        Assert.IsType<ToolCallEvent>(seen[1]);
        Assert.IsType<UsageEvent>(seen[2]);

        (string distinctId, string eventName, Dictionary<string, object> props) =
            Assert.Single(analytics.Captured, capture => capture.EventName == "$ai_generation");

        Assert.Equal("auth0|user-1", distinctId);
        Assert.Equal("gpt-4.1-mini", props["$ai_model"]);
        Assert.Equal(120, props["$ai_input_tokens"]);
        Assert.Equal(45, props["$ai_output_tokens"]);

        (_, _, Dictionary<string, object> toolProps) =
            Assert.Single(analytics.Captured, capture => capture.EventName == "tool_used");

        Assert.Equal("search", toolProps["tool"]);
    }
}
```

- [ ] **Step 3: Run to verify compile failure**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~TelemetryAgentRunnerTests"`
Expected: build error — `TelemetryAgentRunner` not found.

- [ ] **Step 4: Create `TelemetryAgentRunner.cs`**

```csharp
using System.Diagnostics;
using System.Runtime.CompilerServices;

using Chat.Application.Abstractions.Analytics;
using Chat.Application.Abstractions.Turns;

namespace Chat.Application.Turns;

/// <summary>
/// Pass-through decorator (spec Rule 3): zero added latency on the token path; analytics fire
/// after the run. Deleting its DI registration removes PostHog with no other change.
/// If the inner runner throws, nothing is captured — failures are visible in logs and message state.
/// </summary>
public sealed class TelemetryAgentRunner(IAgentRunner inner, IAnalytics analytics) : IAgentRunner
{
    public async IAsyncEnumerable<TurnEvent> RunAsync
    (
        TurnContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        long startTimestamp = Stopwatch.GetTimestamp();
        List<string> tools = [];
        UsageEvent? usage = null;

        await foreach (TurnEvent turnEvent in inner.RunAsync(context, cancellationToken))
        {
            if (turnEvent is ToolCallEvent toolCall)
            {
                tools.Add(toolCall.Tool);
            }

            if (turnEvent is UsageEvent usageEvent)
            {
                usage = usageEvent;
            }

            yield return turnEvent;
        }

        // PostHog's LLM analytics schema → cost/latency/model dashboards out of the box.
        analytics.Capture(context.UserId, "$ai_generation", new Dictionary<string, object>
        {
            ["$ai_model"] = usage?.Model ?? context.ExternalModelId,
            ["$ai_input_tokens"] = usage?.InputTokens ?? 0,
            ["$ai_output_tokens"] = usage?.OutputTokens ?? 0,
            ["$ai_latency"] = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
            ["$ai_trace_id"] = context.TurnId.ToString(),
            ["conversation_id"] = context.ChatId.ToString(),
            ["tools_used"] = tools
        });

        foreach (string tool in tools)
        {
            analytics.Capture(context.UserId, "tool_used", new Dictionary<string, object>
            {
                ["tool"] = tool,
                ["model"] = usage?.Model ?? context.ExternalModelId,
                ["conversation_id"] = context.ChatId.ToString()
            });
        }
    }
}
```

- [ ] **Step 5: Run the decorator test to verify it passes**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~TelemetryAgentRunnerTests"`
Expected: PASS.

- [ ] **Step 6: Add the PostHog package and the two `IAnalytics` implementations**

Run: `dotnet add src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj package PostHog --prerelease`

`NullAnalytics.cs`:

```csharp
using Chat.Application.Abstractions.Analytics;

namespace Chat.Infrastructure.Analytics;

/// <summary>Used when no PostHog key is configured — the pipeline must never depend on analytics.</summary>
internal sealed class NullAnalytics : IAnalytics
{
    public void Capture(string distinctId, string eventName, Dictionary<string, object> properties)
    {
        // Intentionally empty.
    }
}
```

`PostHogAnalytics.cs` — the only file allowed to reference the PostHog SDK. The SDK batches and sends asynchronously, which is what keeps analytics out of the hot path. If the beta SDK's API surface differs from below, adapt this file only:

```csharp
using Chat.Application.Abstractions.Analytics;

using PostHog;

namespace Chat.Infrastructure.Analytics;

internal sealed class PostHogAnalytics(IPostHogClient client) : IAnalytics
{
    public void Capture(string distinctId, string eventName, Dictionary<string, object> properties) =>
        client.Capture(distinctId, eventName, properties);
}
```

- [ ] **Step 7: Build, then commit**

Run: `dotnet build src/services/Chat/Chat.Infrastructure && dotnet test tests/Chat/Chat.Application.Tests`
Expected: build success, all tests pass.

```bash
git add src/services/Chat tests/Chat Directory.Packages.props
git commit -m "feat(chat): add telemetry decorator with posthog llm analytics adapter"
```

---

## Task 9: Turn consumer and DI composition

Wires the worker side: the MassTransit consumer (a one-liner delegating to the orchestrator — keep it that way) and a new `AddTurnWorkerInfrastructure` composition root. Also registers the stream reader for the API side.

**Files:**
- Create: `src/services/Chat/Chat.Infrastructure/Turns/Consumers/TurnRequestedConsumer.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Turns/Consumers/TurnRequestedConsumerDefinition.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Create `TurnRequestedConsumer.cs`** — ack/retry semantics only; all logic lives in the orchestrator:

```csharp
using Chat.Application.Turns;

using MassTransit;

namespace Chat.Infrastructure.Turns.Consumers;

internal sealed class TurnRequestedConsumer(ChatTurnOrchestrator orchestrator) : IConsumer<TurnRequested>
{
    public Task Consume(ConsumeContext<TurnRequested> context) =>
        orchestrator.RunTurnAsync(context.Message, context.CancellationToken);
}
```

- [ ] **Step 2: Create `TurnRequestedConsumerDefinition.cs`** — `ConcurrentMessageLimit` is the worker's capacity knob (in-flight LLM calls per replica); retry covers only pre-stream transient failures because the orchestrator swallows agent failures by design:

```csharp
using MassTransit;

namespace Chat.Infrastructure.Turns.Consumers;

internal sealed class TurnRequestedConsumerDefinition : ConsumerDefinition<TurnRequestedConsumer>
{
    public TurnRequestedConsumerDefinition()
    {
        // In-flight LLM calls per worker replica. Scale replicas, then revisit this knob.
        ConcurrentMessageLimit = 4;
    }

    protected override void ConfigureConsumer
    (
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<TurnRequestedConsumer> consumerConfigurator,
        IRegistrationContext context
    )
    {
        endpointConfigurator.UseMessageRetry(retry =>
            retry.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
    }
}
```

- [ ] **Step 3: Extend `DependencyInjection.cs`.** Add these usings to the existing file:

```csharp
using Chat.Application.Abstractions.Analytics;
using Chat.Application.Abstractions.Turns;
using Chat.Application.Turns;
using Chat.Infrastructure.Agents;
using Chat.Infrastructure.Analytics;
using Chat.Infrastructure.Turns;
using Chat.Infrastructure.Turns.Consumers;

using PostHog;
```

Change the existing API entry point to also register the stream reader (the API serves SSE):

```csharp
    public static IServiceCollection
        AddInfrastructure(this IServiceCollection services, IConfiguration configuration) =>
        services
            .AddSharedInfrastructure()
            .AddAuth0JwtAuthentication(configuration)
            .AddDatabaseServices()
            .AddCacheServices(configuration)
            .AddReaders()
            .AddMessagingServices(configuration)
            .AddTurnStreamReading()
            .AddProviderLogoStorage(configuration);
```

Then append these methods inside the class:

```csharp
    public static IServiceCollection AddTurnWorkerInfrastructure(this IServiceCollection services, IConfiguration configuration) =>
        services
            .AddSharedInfrastructure()
            .AddDatabaseServices()
            .AddTurnPipeline(configuration)
            .AddTurnWorkerMessaging(configuration);

    private static IServiceCollection AddTurnStreamReading(this IServiceCollection services)
    {
        services.AddSingleton<ITurnStreamReader, RedisTurnStreamReader>();

        return services;
    }

    private static IServiceCollection AddTurnPipeline(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<AgentOptions>()
            .Bind(configuration.GetSection(AgentOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<ChatTurnOrchestrator>();
        services.AddScoped<IContextBuilder, ContextBuilder>();
        services.AddSingleton<IMemoryRetriever, NoOpMemoryRetriever>();
        services.AddSingleton<ITokenPublisher, RedisStreamTokenPublisher>();

        // Decorator stack (spec Rule 3): delete the TelemetryAgentRunner registration below
        // (and AddAnalytics) and PostHog is gone — nothing else changes.
        services.AddScoped<AgentFrameworkRunner>();
        services.AddScoped<IAgentRunner>(sp => new TelemetryAgentRunner(
            sp.GetRequiredService<AgentFrameworkRunner>(),
            sp.GetRequiredService<IAnalytics>()));

        services.AddAnalytics(configuration);

        return services;
    }

    private static IServiceCollection AddAnalytics(this IServiceCollection services, IConfiguration configuration)
    {
        string? postHogApiKey = configuration["PostHog:ProjectApiKey"];

        if (string.IsNullOrWhiteSpace(postHogApiKey))
        {
            services.AddSingleton<IAnalytics, NullAnalytics>();
            return services;
        }

        // If the PostHog beta SDK's registration/option names differ, adapt here and in
        // PostHogAnalytics only — no other file may reference the SDK.
        services.AddSingleton<IPostHogClient>(_ => new PostHogClient(new PostHogOptions
        {
            ProjectApiKey = postHogApiKey,
            HostUrl = new Uri(configuration["PostHog:HostUrl"] ?? "https://eu.i.posthog.com")
        }));

        services.AddSingleton<IAnalytics, PostHogAnalytics>();

        return services;
    }

    private static IServiceCollection AddTurnWorkerMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IMessageBus, MessageBus>();

        services.AddMassTransit(configurator =>
        {
            configurator.SetKebabCaseEndpointNameFormatter();

            configurator.AddConsumer<TurnRequestedConsumer, TurnRequestedConsumerDefinition>();

            configurator.AddEntityFrameworkOutbox<ChatDbContext>(outbox =>
            {
                outbox.UsePostgres();
                outbox.UseBusOutbox();
            });

            configurator.AddConfigureEndpointsCallback((context, _, endpointConfigurator) =>
            {
                endpointConfigurator.UseEntityFrameworkOutbox<ChatDbContext>(context);
            });

            configurator.UsingRabbitMq((context, rabbitMqConfigurator) =>
            {
                string rabbitMqConnectionString = configuration.GetConnectionString("rabbitmq")
                    ?? throw new InvalidOperationException("Connection string 'rabbitmq' is required.");

                rabbitMqConfigurator.Host(new Uri(rabbitMqConnectionString));
                rabbitMqConfigurator.ConfigureEndpoints(context);
            });
        });

        return services;
    }
```

> Note: the API's existing `AddMessagingServices` is untouched — the API keeps its user-event consumers and gains nothing turn-related except the stream reader. The worker registers **only** the turn consumer. This is what keeps API and worker independently scalable.

- [ ] **Step 4: Build**

Run: `dotnet build src/services/Chat/Chat.Infrastructure`
Expected: success.

- [ ] **Step 5: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure
git commit -m "feat(chat): add turn consumer and worker/api di composition"
```

---

## Task 10: Chat.TurnWorker project and AppHost wiring

**Files:**
- Create: `src/services/Chat/Chat.TurnWorker/Chat.TurnWorker.csproj`
- Create: `src/services/Chat/Chat.TurnWorker/Program.cs`
- Create: `src/services/Chat/Chat.TurnWorker/appsettings.json`
- Modify: `Nova.AppHost/Nova.AppHost.csproj`
- Modify: `Nova.AppHost/AppHost.cs`

- [ ] **Step 1: Create `Chat.TurnWorker.csproj`** (the ServiceDefaults relative path matches `Chat.Api.csproj` — verify against it; from `src/services/Chat/Chat.TurnWorker/` the root is four levels up):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />

    <PackageReference Include="Aspire.Npgsql" />
    <PackageReference Include="Aspire.Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="Aspire.StackExchange.Redis" />
    <PackageReference Include="EFCore.NamingConventions" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\..\Nova.ServiceDefaults\Nova.ServiceDefaults.csproj" />
    <ProjectReference Include="..\Chat.Application\Chat.Application.csproj" />
    <ProjectReference Include="..\Chat.Infrastructure\Chat.Infrastructure.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create `Program.cs`** (DbContext setup mirrors `Chat.Api/Program.cs`):

```csharp
using Chat.Application;
using Chat.Infrastructure;
using Chat.Infrastructure.Database;

using Microsoft.EntityFrameworkCore;

using Npgsql;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDataSource("chat-db");
builder.AddRedisClient("redis");

builder.Services.AddDbContext<ChatDbContext>((sp, options) =>
{
    NpgsqlDataSource dataSource = sp.GetRequiredService<NpgsqlDataSource>();

    options
        .UseNpgsql(dataSource)
        .UseSnakeCaseNamingConvention();
});

builder.EnrichNpgsqlDbContext<ChatDbContext>();

builder.Services.AddApplication();
builder.Services.AddTurnWorkerInfrastructure(builder.Configuration);

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

await app.RunAsync();
```

- [ ] **Step 3: Create `appsettings.json`**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Agent": {
    "BaseUrl": "https://openrouter.ai/api/v1"
  }
}
```

- [ ] **Step 4: Add to the solution and AppHost project references**

```bash
dotnet sln Nova.slnx add src/services/Chat/Chat.TurnWorker/Chat.TurnWorker.csproj
```

In `Nova.AppHost/Nova.AppHost.csproj`, add alongside the existing project references:

```xml
    <ProjectReference Include="..\src\services\Chat\Chat.TurnWorker\Chat.TurnWorker.csproj" />
```

- [ ] **Step 5: Wire the worker in `Nova.AppHost/AppHost.cs`.** After the `chatApi` block, add:

```csharp
IResourceBuilder<ParameterResource> agentApiKey = builder.AddParameter("agent-api-key", secret: true);

IResourceBuilder<ProjectResource> chatTurnWorker = builder.AddProject<Projects.Chat_TurnWorker>("chat-turn-worker")
    .WithEnvironment("Agent__ApiKey", agentApiKey)
    .WithReference(redis)
    .WithReference(chatDb)
    .WithReference(rabbitMq)
    .WaitFor(redis)
    .WaitFor(rabbitMq)
    .WaitForCompletion(chatMigrations);
```

Also add `builder.AddRedisClient("redis");` to `src/services/Chat/Chat.Api/Program.cs`, directly after the existing `builder.AddRedisDistributedCache("redis");` line (the SSE reader needs `IConnectionMultiplexer`).

- [ ] **Step 6: Set the agent key parameter** (tell the user to run this with their real key — OpenRouter or any OpenAI-compatible endpoint):

```bash
dotnet user-secrets set Parameters:agent-api-key "<your-key>" --project Nova.AppHost
```

- [ ] **Step 7: Build the solution**

Run: `dotnet build`
Expected: success including the new worker project.

- [ ] **Step 8: Commit**

```bash
git add src/services/Chat/Chat.TurnWorker src/services/Chat/Chat.Api/Program.cs Nova.AppHost Nova.slnx
git commit -m "feat(chat): add chat turn worker service with apphost wiring"
```

---## Task 11: API endpoints — create chat, send message, SSE stream

**Files:**
- Modify: `src/services/Chat/Chat.Api/Endpoints/CustomTags.cs` (add a `Chats` constant following the existing pattern)
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/Responses/TurnStartedResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/CreateChat/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/SendMessage/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/StreamTurn/Endpoint.cs`

- [ ] **Step 1: Add the `Chats` tag constant** to `CustomTags.cs`, matching the style of the existing constants:

```csharp
    public const string Chats = "Chats";
```

(Adjust accessibility to match the file's existing members.)

- [ ] **Step 2: Create `TurnStartedResponse.cs`**

```csharp
using Chat.Application.Chats.Results;

namespace Chat.Api.Endpoints.Chats.Responses;

public sealed record TurnStartedResponse
(
    Guid ChatId,
    Guid UserMessageId,
    Guid AssistantMessageId,
    string StreamPath
)
{
    public static TurnStartedResponse From(TurnStartedResult result) => new
    (
        ChatId: result.ChatId,
        UserMessageId: result.UserMessageId,
        AssistantMessageId: result.AssistantMessageId,
        StreamPath: $"/v1/chats/{result.ChatId}/turns/{result.AssistantMessageId}/stream"
    );
}
```

- [ ] **Step 3: Create the CreateChat endpoint** (`Endpoints/Chats/CreateChat/Endpoint.cs`)

```csharp
using Chat.Api.Endpoints.Chats.Responses;
using Chat.Application.Chats.Commands.CreateChat;
using Chat.Application.Chats.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.CreateChat;

internal sealed record Request(string Message, Guid ModelId);

internal sealed class Endpoint(ISender sender) : Endpoint<Request>
{
    public const string RouteName = "Chat.Chats.Create";

    public override void Configure()
    {
        Post("/chats");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Create Chat")
                .WithDescription("Creates a chat with the first user message and starts generating the assistant reply asynchronously.")
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
        CreateChatCommand command = new(request.Message, request.ModelId);

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

- [ ] **Step 4: Create the SendMessage endpoint** (`Endpoints/Chats/SendMessage/Endpoint.cs`)

```csharp
using Chat.Api.Endpoints.Chats.Responses;
using Chat.Application.Chats.Commands.SendMessage;
using Chat.Application.Chats.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.SendMessage;

internal sealed record Request(string Message, Guid ModelId);

internal sealed class Endpoint(ISender sender) : Endpoint<Request>
{
    public const string RouteName = "Chat.Chats.SendMessage";

    public override void Configure()
    {
        Post("/chats/{chatId}/messages");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Send Message")
                .WithDescription("Appends a user message to the active branch and starts generating the assistant reply asynchronously.")
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
        SendMessageCommand command = new(Route<Guid>("chatId"), request.Message, request.ModelId);

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

- [ ] **Step 5: Create the SSE stream endpoint** (`Endpoints/Chats/StreamTurn/Endpoint.cs`).

Design notes baked into this code — don't change them casually:
- It authorizes by loading the thread for the **authenticated** user (404 otherwise — no information leak about other users' chats).
- For an already-terminal turn it emits a single synthetic `done`/`failed` event (the stream may have expired); the client then refetches the message through the (future) read endpoints.
- It injects `IChatRepository`/`IUserContext` directly instead of going through Mediator — streaming responses don't fit the command/response pipeline, and that's an accepted, deliberate exception.

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
                .WithDescription("Streams turn events (tokens, usage, done/failed) for an assistant message as Server-Sent Events. Supports resume via the Last-Event-ID header.")
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
        // The client refetches the message content through the read endpoints.
        if (message.Status == MessageStatus.Completed)
        {
            await WriteEventAsync("terminal", new DoneEvent(message.Id.Value), ct);
            return;
        }

        if (message.Status == MessageStatus.Failed)
        {
            await WriteEventAsync
            (
                "terminal",
                new FailedEvent(message.Id.Value, message.FailureReason?.Value ?? "The turn failed."),
                ct
            );
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

- [ ] **Step 6: Build**

Run: `dotnet build src/services/Chat/Chat.Api`
Expected: success.

- [ ] **Step 7: Commit**

```bash
git add src/services/Chat/Chat.Api
git commit -m "feat(chat): add create-chat, send-message, and sse turn stream endpoints"
```

---

## Task 12: Full verification

- [ ] **Step 1: Build everything and run every test project**

```bash
dotnet build
dotnet test
```

Expected: clean build; all domain, application, and infrastructure tests pass.

- [ ] **Step 2: Verify no EF model drift** (this plan must not require a migration — the turn lifecycle reuses existing columns):

Run: `dotnet ef migrations has-pending-model-changes --project src/services/Chat/Chat.Infrastructure --startup-project src/workers/Chat.MigrationWorker`
Expected: no pending model changes. If there ARE pending changes, something added state to the domain that the spec forbids (Rule 7) — stop and review before generating any migration.

- [ ] **Step 3: End-to-end smoke test (manual, requires the user's LLM API key from Task 10 Step 6).**

Start the stack: `aspire run` (or `dotnet run --project Nova.AppHost`).

Then in another terminal (replace `<model-id>` with an enabled model's GUID from the catalog, and use a valid bearer token / the BFF route as appropriate for the auth setup):

```bash
# 1. Create a chat — expect 201 with chatId / assistantMessageId / streamPath
curl -sk -X POST https://localhost:7201/v1/chats \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"message": "Explain Redis Streams in two sentences.", "modelId": "<model-id>"}'

# 2. Open the SSE stream — expect token events arriving live, then usage, then done
curl -skN https://localhost:7201<streamPath-from-step-1> \
  -H "Authorization: Bearer <token>"

# 3. Re-run the same stream request — expect a full replay of the same events
#    (or a single terminal "done" if the stream TTL has passed)

# 4. Send a follow-up — expect 202; while it generates, send another → expect 409 Chat.ParentStillGenerating
```

Also verify in the Aspire dashboard that `chat-turn-worker` is healthy, and (if a PostHog key is configured) that an `$ai_generation` event arrived in PostHog.

- [ ] **Step 4: Final commit if anything was touched during verification**

```bash
git add -A
git commit -m "chore(chat): verification fixes for the turn pipeline"
```

---

## Post-Plan Checklist (from the spec — answer before ANY future addition to this pipeline)

1. Which **seam** does the change live behind? No seam → design one first.
2. Does any framework type leak past `Chat.Infrastructure/Agents/`? (Rule 1)
3. Did `ChatTurnOrchestrator` gain logic? (Rule 2 — it must not)
4. Can the change be removed by deleting one DI line or one file? (Rule 3)
5. Does `TurnEvent` change shape rather than grow? (Rule 6 — shape changes are forbidden)
6. Is a new entity being added for state the aggregate already owns? (Rule 7)

## Explicitly Out of Scope (do not "helpfully" add)

- Memory retrieval/extraction (Rule 8) — `NoOpMemoryRetriever` stays.
- Agent tools (Rule 5) — contract events exist; no tool execution.
- Chat read queries (list chats, active path, tree), edit/regenerate/select endpoints — they belong to the older chat-tree plan (`docs/superpowers/plans/2026-06-09-chat-tree-streaming-implementation.md`), to be revisited after this pipeline lands.
- Title generation, memory-extraction follow-up jobs, turn cancellation.
- Upgrading MassTransit. Ever. (AGENTS.md)
