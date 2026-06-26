# Stop Generation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add user-requested stop generation so an in-flight assistant message becomes terminal and preserves the text generated so far.

**Architecture:** The assistant `ChatMessage` remains the durable turn lifecycle. A Redis-backed `ITurnStopSignal` carries the cooperative stop request; `ChatTurnOrchestrator` observes that seam while streaming, then transitions the aggregate through `ChatThread.StopAssistantMessage(...)` and publishes an append-only `StoppedEvent`. The `IContextBuilder` interface and responsibilities stay unchanged, but its existing history predicate must include stopped messages that have content so follow-up turns see the partial assistant answer.

**Tech Stack:** .NET 10, Mediator.SourceGenerator, FastEndpoints, ErrorOr, EF Core + Npgsql, StackExchange.Redis via Aspire, MassTransit 8.4.1 pinned, xUnit.

---

## Test Approval Gate

`AGENTS.md` says not to write, modify, or expand tests without asking the user first. This implementation plan includes exact test steps because they are the safest way to implement the feature, but the executor must ask for approval before executing any test-writing step.

Ask this before Task 1 Step 1:

> Do you approve adding focused tests for stop generation covering the domain transition, stream event serialization, stop command handler, orchestrator stop behavior, and context-builder inclusion of stopped content?

If the user says yes, follow the test steps. If the user says no, skip the test-writing steps and still run build/compile verification after implementation.

Any `dotnet build`, `dotnet test`, `dotnet restore`, or `dotnet run` command must be requested with elevated permissions per `AGENTS.md`.

---

## File Structure

Domain lifecycle:

- Modify: `src/services/Chat/Chat.Domain/Chats/ValueObjects/MessageStatus.cs`
- Modify: `src/services/Chat/Chat.Domain/Chats/ChatErrors.cs`
- Modify: `src/services/Chat/Chat.Domain/Chats/Entities/ChatMessage.cs`
- Modify: `src/services/Chat/Chat.Domain/Chats/ChatThread.cs`
- Test with approval: `tests/Chat/Chat.Domain.Tests/Chats/ChatThreadTests.cs`

Stream contract:

- Modify: `src/services/Chat/Chat.Application/Turns/TurnEvent.cs`
- Modify: `src/services/Chat/Chat.Application/Turns/TurnEventSerializer.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Turns/RedisTurnStreamReader.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Turns/RedisStreamTokenPublisher.cs`
- Test with approval: `tests/Chat/Chat.Application.Tests/Turns/TurnEventSerializerTests.cs`

Stop signal:

- Create: `src/services/Chat/Chat.Application/Abstractions/Turns/ITurnStopSignal.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Turns/RedisTurnStopSignal.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`
- Test helper with approval: `tests/Chat/Chat.Application.Tests/Turns/FakeTurnStopSignal.cs`

Stop command and endpoint:

- Create: `src/services/Chat/Chat.Application/Chats/Commands/StopGeneration/StopGenerationCommand.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/StopGeneration/StopGenerationCommandValidator.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/StopGeneration/StopGenerationHandler.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/StopGeneration/Endpoint.cs`
- Test with approval: `tests/Chat/Chat.Application.Tests/Turns/StopGenerationHandlerTests.cs`

Orchestrator and context:

- Modify: `src/services/Chat/Chat.Application/Turns/ChatTurnOrchestrator.cs`
- Modify: `src/services/Chat/Chat.Application/Turns/ContextBuilder.cs`
- Test with approval: `tests/Chat/Chat.Application.Tests/Turns/ChatTurnOrchestratorTests.cs`
- Test with approval: `tests/Chat/Chat.Application.Tests/Turns/ContextBuilderTests.cs`

No EF migration is required: `MessageStatus` is stored as a string and no schema shape changes.

---

## Task 1: Domain Stopped Lifecycle

**Files:**
- Modify: `src/services/Chat/Chat.Domain/Chats/ValueObjects/MessageStatus.cs`
- Modify: `src/services/Chat/Chat.Domain/Chats/ChatErrors.cs`
- Modify: `src/services/Chat/Chat.Domain/Chats/Entities/ChatMessage.cs`
- Modify: `src/services/Chat/Chat.Domain/Chats/ChatThread.cs`
- Test with approval: `tests/Chat/Chat.Domain.Tests/Chats/ChatThreadTests.cs`

- [ ] **Step 1: If tests are approved, add the stopped lifecycle tests**

Append these tests to `tests/Chat/Chat.Domain.Tests/Chats/ChatThreadTests.cs`:

```csharp
[Fact]
public void StopAssistantMessageWhenGeneratingStoresPartialContentAndMarksStopped()
{
    ChatThread chat = TestChatFactory.CreateThread();
    ChatMessage assistant = BeginAssistant(chat);
    MessageContent partial = TestChatFactory.CreateContent("Partial answer");
    DateTimeOffset stoppedAt = TestChatFactory.CreatedAt.AddMinutes(2);

    ErrorOr<ChatMessage> result = chat.StopAssistantMessage(assistant.Id, partial, stoppedAt);

    Assert.False(result.IsError);
    Assert.Equal(MessageStatus.Stopped, assistant.Status);
    Assert.Equal("Partial answer", assistant.Content!.Value);
    Assert.Equal(stoppedAt, assistant.CompletedAt);
    Assert.Equal(stoppedAt, chat.UpdatedAt);
}

[Fact]
public void StopAssistantMessageWhenNoTextGeneratedMarksStoppedWithNullContent()
{
    ChatThread chat = TestChatFactory.CreateThread();
    ChatMessage assistant = BeginAssistant(chat);
    DateTimeOffset stoppedAt = TestChatFactory.CreatedAt.AddMinutes(2);

    ErrorOr<ChatMessage> result = chat.StopAssistantMessage(assistant.Id, content: null, stoppedAt: stoppedAt);

    Assert.False(result.IsError);
    Assert.Equal(MessageStatus.Stopped, assistant.Status);
    Assert.Null(assistant.Content);
    Assert.Equal(stoppedAt, assistant.CompletedAt);
}

[Fact]
public void StopAssistantMessageReturnsErrorWhenTargetIsUserMessage()
{
    ChatThread chat = TestChatFactory.CreateThread();

    ErrorOr<ChatMessage> result = chat.StopAssistantMessage
    (
        messageId: chat.CurrentMessageId,
        content: null,
        stoppedAt: TestChatFactory.CreatedAt.AddMinutes(1)
    );

    AssertError(result, ErrorType.Conflict, "Chat.StopTargetMustBeAssistant");
}

[Fact]
public void StopAssistantMessageReturnsErrorWhenAssistantAlreadyTerminal()
{
    ChatThread chat = TestChatFactory.CreateThread();
    ChatMessage assistant = BeginAssistant(chat);
    chat.CompleteAssistantMessage
    (
        messageId: assistant.Id,
        content: TestChatFactory.CreateContent("Done"),
        completedAt: TestChatFactory.CreatedAt.AddMinutes(2)
    );

    ErrorOr<ChatMessage> result = chat.StopAssistantMessage
    (
        messageId: assistant.Id,
        content: TestChatFactory.CreateContent("Late"),
        stoppedAt: TestChatFactory.CreatedAt.AddMinutes(3)
    );

    AssertError(result, ErrorType.Conflict, "Chat.CannotStopNonGenerating");
}
```

- [ ] **Step 2: If tests are approved, run the focused domain tests and verify they fail**

Run with elevated permissions:

```bash
dotnet test tests/Chat/Chat.Domain.Tests --filter "FullyQualifiedName~ChatThreadTests"
```

Expected: FAIL because `MessageStatus.Stopped` and `StopAssistantMessage` do not exist yet.

- [ ] **Step 3: Add the stopped status**

Update `src/services/Chat/Chat.Domain/Chats/ValueObjects/MessageStatus.cs`:

```csharp
namespace Chat.Domain.Chats.ValueObjects;

#pragma warning disable CA1008
public enum MessageStatus
{
    Generating = 1,
    Completed = 2,
    Failed = 3,
    Stopped = 4
}
```

- [ ] **Step 4: Add stop-specific domain errors**

Append these members inside `ChatErrors` in `src/services/Chat/Chat.Domain/Chats/ChatErrors.cs`:

```csharp
public static Error StopTargetMustBeAssistant(ChatMessageId messageId) =>
    Error.Conflict
    (
        code: "Chat.StopTargetMustBeAssistant",
        description: $"Only assistant messages can be stopped; '{messageId.Value}' is not an assistant message."
    );

public static Error CannotStopNonGenerating(ChatMessageId messageId) =>
    Error.Conflict
    (
        code: "Chat.CannotStopNonGenerating",
        description: $"Message '{messageId.Value}' is not generating and cannot be stopped."
    );
```

- [ ] **Step 5: Add the entity transition**

Add this method to `src/services/Chat/Chat.Domain/Chats/Entities/ChatMessage.cs` next to `CompleteAssistantMessage` and `Fail`:

```csharp
internal ErrorOr<Success> Stop(MessageContent? content, DateTimeOffset stoppedAt)
{
    if (Status != MessageStatus.Generating)
    {
        return ChatErrors.CannotStopNonGenerating(Id);
    }

    Content = content;
    Status = MessageStatus.Stopped;
    CompletedAt = stoppedAt;

    return Result.Success;
}
```

- [ ] **Step 6: Add the aggregate transition**

Add this method to `src/services/Chat/Chat.Domain/Chats/ChatThread.cs` after `FailAssistantMessage`:

```csharp
public ErrorOr<ChatMessage> StopAssistantMessage
(
    ChatMessageId messageId,
    MessageContent? content,
    DateTimeOffset stoppedAt
)
{
    ChatMessage? message = FindMessage(messageId);

    if (message is null)
    {
        return ChatErrors.MessageNotFound(messageId);
    }

    if (message.Role != MessageRole.Assistant)
    {
        return ChatErrors.StopTargetMustBeAssistant(messageId);
    }

    ErrorOr<Success> result = message.Stop(content, stoppedAt);

    if (result.IsError)
    {
        return result.Errors;
    }

    UpdatedAt = stoppedAt;

    return message;
}
```

- [ ] **Step 7: If tests are approved, run the focused domain tests and verify they pass**

Run with elevated permissions:

```bash
dotnet test tests/Chat/Chat.Domain.Tests --filter "FullyQualifiedName~ChatThreadTests"
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/services/Chat/Chat.Domain tests/Chat/Chat.Domain.Tests/Chats/ChatThreadTests.cs
git commit -m "feat(chat): add stopped assistant message lifecycle"
```

If tests were not approved, omit the test path from `git add`.

---

## Task 2: Append Stopped Stream Event

**Files:**
- Modify: `src/services/Chat/Chat.Application/Turns/TurnEvent.cs`
- Modify: `src/services/Chat/Chat.Application/Turns/TurnEventSerializer.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Turns/RedisTurnStreamReader.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Turns/RedisStreamTokenPublisher.cs`
- Test with approval: `tests/Chat/Chat.Application.Tests/Turns/TurnEventSerializerTests.cs`

- [ ] **Step 1: If tests are approved, add `StoppedEvent` to serializer coverage**

In `TurnEventSerializerTests.Events`, add:

```csharp
{ new StoppedEvent(TurnId), "stopped" }
```

- [ ] **Step 2: If tests are approved, run the serializer test and verify it fails**

Run with elevated permissions:

```bash
dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~TurnEventSerializerTests"
```

Expected: FAIL because `StoppedEvent` does not exist.

- [ ] **Step 3: Add `StoppedEvent` append-only to `TurnEvent`**

Update `src/services/Chat/Chat.Application/Turns/TurnEvent.cs`:

```csharp
[JsonDerivedType(typeof(StoppedEvent), "stopped")]
```

Add this record after `FailedEvent`:

```csharp
public sealed record StoppedEvent(Guid TurnId) : TurnEvent(TurnId);
```

Do not alter existing records or existing discriminator strings.

- [ ] **Step 4: Teach the serializer the event name**

Update the switch in `src/services/Chat/Chat.Application/Turns/TurnEventSerializer.cs`:

```csharp
StoppedEvent => "stopped",
```

Place it before the fallback arm.

- [ ] **Step 5: Make the Redis stream reader stop on `StoppedEvent`**

Update the terminal check in `src/services/Chat/Chat.Infrastructure/Turns/RedisTurnStreamReader.cs`:

```csharp
if (turnEvent is DoneEvent or FailedEvent or StoppedEvent)
{
    yield break;
}
```

- [ ] **Step 6: Make the Redis publisher expire stopped streams**

Update the TTL condition in `src/services/Chat/Chat.Infrastructure/Turns/RedisStreamTokenPublisher.cs`:

```csharp
if (turnEvent is DoneEvent or FailedEvent or StoppedEvent)
{
    await db.KeyExpireAsync(key, CompletedStreamTtl);
}
```

- [ ] **Step 7: If tests are approved, run serializer tests**

Run with elevated permissions:

```bash
dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~TurnEventSerializerTests"
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/services/Chat/Chat.Application/Turns/TurnEvent.cs src/services/Chat/Chat.Application/Turns/TurnEventSerializer.cs src/services/Chat/Chat.Infrastructure/Turns/RedisTurnStreamReader.cs src/services/Chat/Chat.Infrastructure/Turns/RedisStreamTokenPublisher.cs tests/Chat/Chat.Application.Tests/Turns/TurnEventSerializerTests.cs
git commit -m "feat(chat): add stopped turn stream event"
```

If tests were not approved, omit the test path from `git add`.

---

## Task 3: Stop Signal Seam And Redis Implementation

**Files:**
- Create: `src/services/Chat/Chat.Application/Abstractions/Turns/ITurnStopSignal.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Turns/RedisTurnStopSignal.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`
- Test helper with approval: `tests/Chat/Chat.Application.Tests/Turns/FakeTurnStopSignal.cs`

- [ ] **Step 1: Create the application seam**

Create `src/services/Chat/Chat.Application/Abstractions/Turns/ITurnStopSignal.cs`:

```csharp
namespace Chat.Application.Abstractions.Turns;

public interface ITurnStopSignal
{
    Task RequestStopAsync(Guid assistantMessageId, CancellationToken cancellationToken);

    Task<bool> IsStopRequestedAsync(Guid assistantMessageId, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Create the Redis implementation**

Create `src/services/Chat/Chat.Infrastructure/Turns/RedisTurnStopSignal.cs`:

```csharp
using Chat.Application.Abstractions.Turns;

using StackExchange.Redis;

namespace Chat.Infrastructure.Turns;

internal sealed class RedisTurnStopSignal(IConnectionMultiplexer redis) : ITurnStopSignal
{
    private const string KeyPrefix = "chat:turn-stop:";

    private static readonly TimeSpan StopSignalTtl = TimeSpan.FromMinutes(10);

    private static string StopKey(Guid assistantMessageId) => $"{KeyPrefix}{assistantMessageId}";

    public async Task RequestStopAsync(Guid assistantMessageId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        await redis
            .GetDatabase()
            .StringSetAsync(StopKey(assistantMessageId), "1", StopSignalTtl);
    }

    public async Task<bool> IsStopRequestedAsync(Guid assistantMessageId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        return await redis
            .GetDatabase()
            .KeyExistsAsync(StopKey(assistantMessageId));
    }
}
```

- [ ] **Step 3: Register the seam for API and worker**

In `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`, call a new helper from both `AddInfrastructure` and `AddTurnWorkerInfrastructure`.

Update `AddInfrastructure`:

```csharp
.AddMessagingServices(configuration)
.AddTurnStopSignal()
.AddTurnStreamReading()
```

Update `AddTurnWorkerInfrastructure`:

```csharp
.AddDatabaseServices()
.AddTurnStopSignal()
.AddTurnPipeline(configuration)
```

Add the helper near `AddTurnStreamReading`:

```csharp
private static IServiceCollection AddTurnStopSignal(this IServiceCollection services)
{
    services.AddSingleton<ITurnStopSignal, RedisTurnStopSignal>();

    return services;
}
```

- [ ] **Step 4: If tests are approved, create a fake stop signal**

Create `tests/Chat/Chat.Application.Tests/Turns/FakeTurnStopSignal.cs`:

```csharp
using Chat.Application.Abstractions.Turns;

namespace Chat.Application.Tests.Turns;

internal sealed class FakeTurnStopSignal : ITurnStopSignal
{
    private readonly HashSet<Guid> _requestedStops = [];
    private readonly Queue<bool> _scriptedResponses = [];

    public int CheckCount { get; private set; }

    public void Request(Guid assistantMessageId) =>
        _requestedStops.Add(assistantMessageId);

    public void EnqueueResponse(bool isStopRequested) =>
        _scriptedResponses.Enqueue(isStopRequested);

    public Task RequestStopAsync(Guid assistantMessageId, CancellationToken cancellationToken)
    {
        _requestedStops.Add(assistantMessageId);

        return Task.CompletedTask;
    }

    public Task<bool> IsStopRequestedAsync(Guid assistantMessageId, CancellationToken cancellationToken)
    {
        CheckCount++;

        if (_scriptedResponses.TryDequeue(out bool scripted))
        {
            return Task.FromResult(scripted);
        }

        return Task.FromResult(_requestedStops.Contains(assistantMessageId));
    }
}
```

- [ ] **Step 5: Run build verification**

Run with elevated permissions:

```bash
dotnet build Nova.slnx
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/services/Chat/Chat.Application/Abstractions/Turns/ITurnStopSignal.cs src/services/Chat/Chat.Infrastructure/Turns/RedisTurnStopSignal.cs src/services/Chat/Chat.Infrastructure/DependencyInjection.cs tests/Chat/Chat.Application.Tests/Turns/FakeTurnStopSignal.cs
git commit -m "feat(chat): add turn stop signal"
```

If tests were not approved, omit the fake test helper path from `git add`.

---

## Task 4: Stop Generation Command

**Files:**
- Create: `src/services/Chat/Chat.Application/Chats/Commands/StopGeneration/StopGenerationCommand.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/StopGeneration/StopGenerationCommandValidator.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/StopGeneration/StopGenerationHandler.cs`
- Test with approval: `tests/Chat/Chat.Application.Tests/Turns/StopGenerationHandlerTests.cs`

- [ ] **Step 1: If tests are approved, add handler tests**

Create `tests/Chat/Chat.Application.Tests/Turns/StopGenerationHandlerTests.cs`:

```csharp
using Chat.Application.Chats.Commands.StopGeneration;
using Chat.Application.Tests.FavoriteModels;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Turns;

public sealed class StopGenerationHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeChatRepository _chats = new();
    private readonly FakeTurnStopSignal _stopSignal = new();

    [Fact]
    public async Task RequestsStopForGeneratingAssistantMessage()
    {
        (ChatThread thread, ChatMessage assistant) = SeedGeneratingAssistant();

        ErrorOr<Success> result = await CreateHandler()
            .Handle(new StopGenerationCommand(thread.Id.Value, assistant.Id.Value), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(await _stopSignal.IsStopRequestedAsync(assistant.Id.Value, CancellationToken.None));
    }

    [Fact]
    public async Task ReturnsChatNotFoundWhenChatUnknown()
    {
        (_, ChatMessage assistant) = SeedGeneratingAssistant();

        ErrorOr<Success> result = await CreateHandler()
            .Handle(new StopGenerationCommand(Guid.CreateVersion7(), assistant.Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task ReturnsMessageNotFoundWhenMessageUnknown()
    {
        (ChatThread thread, _) = SeedGeneratingAssistant();

        ErrorOr<Success> result = await CreateHandler()
            .Handle(new StopGenerationCommand(thread.Id.Value, Guid.CreateVersion7()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.MessageNotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task ReturnsErrorWhenTargetIsUserMessage()
    {
        (ChatThread thread, _) = SeedGeneratingAssistant();

        ErrorOr<Success> result = await CreateHandler()
            .Handle(new StopGenerationCommand(thread.Id.Value, thread.Messages.Single(x => x.Role == MessageRole.User).Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.StopTargetMustBeAssistant", result.FirstError.Code);
    }

    [Fact]
    public async Task ReturnsErrorWhenAssistantAlreadyTerminal()
    {
        (ChatThread thread, ChatMessage assistant) = SeedGeneratingAssistant();
        thread.CompleteAssistantMessage(assistant.Id, MessageContent.Create("Done").Value, Now);

        ErrorOr<Success> result = await CreateHandler()
            .Handle(new StopGenerationCommand(thread.Id.Value, assistant.Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.CannotStopNonGenerating", result.FirstError.Code);
    }

    private (ChatThread Thread, ChatMessage Assistant) SeedGeneratingAssistant()
    {
        ChatThread thread = ChatThread.Create
        (
            userId: UserId.Create("auth0|user-1").Value,
            title: ChatTitle.Create("Hello").Value,
            firstUserMessage: MessageContent.Create("Hello").Value,
            createdAt: Now
        );

        ChatMessage assistant = thread.BeginAssistantMessage
        (
            parentMessageId: thread.CurrentMessageId,
            llmModelId: LlmModelId.New(),
            createdAt: Now
        ).Value;

        _chats.Seed(thread);

        return (thread, assistant);
    }

    private StopGenerationHandler CreateHandler() => new
    (
        userContext: new FakeUserContext("auth0|user-1"),
        chats: _chats,
        stopSignal: _stopSignal
    );
}
```

- [ ] **Step 2: If tests are approved, run the handler tests and verify they fail**

Run with elevated permissions:

```bash
dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~StopGenerationHandlerTests"
```

Expected: FAIL because the command types do not exist yet.

- [ ] **Step 3: Create the command**

Create `src/services/Chat/Chat.Application/Chats/Commands/StopGeneration/StopGenerationCommand.cs`:

```csharp
using ErrorOr;

using Mediator;

using SharedKernel;

namespace Chat.Application.Chats.Commands.StopGeneration;

public sealed record StopGenerationCommand
(
    Guid ChatId,
    Guid AssistantMessageId
) : ICommand<ErrorOr<Success>>;
```

- [ ] **Step 4: Create the validator**

Create `src/services/Chat/Chat.Application/Chats/Commands/StopGeneration/StopGenerationCommandValidator.cs`:

```csharp
using FluentValidation;

namespace Chat.Application.Chats.Commands.StopGeneration;

internal sealed class StopGenerationCommandValidator : AbstractValidator<StopGenerationCommand>
{
    public StopGenerationCommandValidator()
    {
        RuleFor(x => x.ChatId)
            .NotEmpty();

        RuleFor(x => x.AssistantMessageId)
            .NotEmpty();
    }
}
```

- [ ] **Step 5: Create the handler**

Create `src/services/Chat/Chat.Application/Chats/Commands/StopGeneration/StopGenerationHandler.cs`:

```csharp
using Chat.Application.Abstractions.Turns;
using Chat.Application.Chats.Errors;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

using SharedKernel;

namespace Chat.Application.Chats.Commands.StopGeneration;

internal sealed class StopGenerationHandler(
    IUserContext userContext,
    IChatRepository chats,
    ITurnStopSignal stopSignal) : ICommandHandler<StopGenerationCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(StopGenerationCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(command.ChatId);
        ErrorOr<ChatMessageId> messageIdResult = ChatMessageId.Create(command.AssistantMessageId);

        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
        }

        if (chatIdResult.IsError)
        {
            errors.AddRange(chatIdResult.Errors);
        }

        if (messageIdResult.IsError)
        {
            errors.AddRange(messageIdResult.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        ChatId chatId = chatIdResult.Value;
        UserId userId = userIdResult.Value;
        ChatMessageId messageId = messageIdResult.Value;

        ChatThread? thread = await chats.GetByIdAsync
        (
            id: chatId,
            userId: userId,
            cancellationToken: cancellationToken
        );

        if (thread is null)
        {
            return ChatOperationErrors.ChatNotFound(chatId);
        }

        ChatMessage? target = thread.FindMessage(messageId);

        if (target is null)
        {
            return ChatErrors.MessageNotFound(messageId);
        }

        if (target.Role != MessageRole.Assistant)
        {
            return ChatErrors.StopTargetMustBeAssistant(messageId);
        }

        if (target.Status != MessageStatus.Generating)
        {
            return ChatErrors.CannotStopNonGenerating(messageId);
        }

        await stopSignal.RequestStopAsync(target.Id.Value, cancellationToken);

        return Result.Success;
    }
}
```

- [ ] **Step 6: If tests are approved, run the handler tests**

Run with elevated permissions:

```bash
dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~StopGenerationHandlerTests"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/services/Chat/Chat.Application/Chats/Commands/StopGeneration tests/Chat/Chat.Application.Tests/Turns/StopGenerationHandlerTests.cs
git commit -m "feat(chat): add stop generation command"
```

If tests were not approved, omit the test path from `git add`.

---

## Task 5: FastEndpoints Stop Endpoint

**Files:**
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/StopGeneration/Endpoint.cs`

- [ ] **Step 1: Create the endpoint**

Create `src/services/Chat/Chat.Api/Endpoints/Chats/StopGeneration/Endpoint.cs`:

```csharp
using Chat.Application.Chats.Commands.StopGeneration;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

using SharedKernel;

namespace Chat.Api.Endpoints.Chats.StopGeneration;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest
{
    public const string RouteName = "Chat.Chats.StopGeneration";

    public override void Configure()
    {
        Post("/chats/{chatId}/messages/{assistantMessageId}/stop");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Stop Generation")
                .WithDescription("Requests that a generating assistant message stop and keep any partial content already produced.")
                .Produces(StatusCodes.Status202Accepted)
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .ProducesProblemDetails(StatusCodes.Status409Conflict, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        StopGenerationCommand command = new
        (
            ChatId: Route<Guid>("chatId"),
            AssistantMessageId: Route<Guid>("assistantMessageId")
        );

        ErrorOr<Success> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResultAsync(TypedResults.Accepted());
    }
}
```

- [ ] **Step 2: Run build verification**

Run with elevated permissions:

```bash
dotnet build Nova.slnx
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add src/services/Chat/Chat.Api/Endpoints/Chats/StopGeneration/Endpoint.cs
git commit -m "feat(chat): add stop generation endpoint"
```

---

## Task 6: Orchestrator Cooperative Stop

**Files:**
- Modify: `src/services/Chat/Chat.Application/Turns/ChatTurnOrchestrator.cs`
- Modify with approval: `tests/Chat/Chat.Application.Tests/Turns/ChatTurnOrchestratorTests.cs`

- [ ] **Step 1: If tests are approved, update the orchestrator test fixture**

Add a stop signal field to `ChatTurnOrchestratorTests`:

```csharp
private readonly FakeTurnStopSignal _stopSignal = new();
```

Update `CreateOrchestrator`:

```csharp
private ChatTurnOrchestrator CreateOrchestrator(IAgentRunner runner) => new
(
    chats: _chats,
    memoryRetriever: new FakeMemoryRetriever(),
    publisher: _publisher,
    contextBuilder: new FakeContextBuilder(),
    agentRunner: runner,
    stopSignal: _stopSignal,
    unitOfWork: _unitOfWork,
    dateTimeProvider: new FakeDateTimeProvider(Now),
    logger: NullLogger<ChatTurnOrchestrator>.Instance
);
```

- [ ] **Step 2: If tests are approved, add the partial-content stop test**

Append this test to `ChatTurnOrchestratorTests`:

```csharp
[Fact]
public async Task RunTurnAsyncWhenStopRequestedStoresPartialContentAndPublishesStoppedEvent()
{
    (_, ChatMessage assistant, TurnRequested job) = SeedPendingTurn();
    _stopSignal.EnqueueResponse(false);
    _stopSignal.EnqueueResponse(false);
    _stopSignal.EnqueueResponse(true);

    FakeAgentRunner runner = new(ctx => FakeAgentRunner.Tokens(ctx.TurnId, ["Hello", " world", " ignored"]));

    await CreateOrchestrator(runner).RunTurnAsync(job, CancellationToken.None);

    Assert.Equal(MessageStatus.Stopped, assistant.Status);
    Assert.Equal("Hello world", assistant.Content!.Value);
    Assert.Equal(1, _unitOfWork.SaveCount);
    Assert.Equal(1, _publisher.ResetCount);
    Assert.Equal(2, _publisher.Events.OfType<TokenEvent>().Count());
    Assert.IsType<StoppedEvent>(_publisher.Events[^1]);
}
```

- [ ] **Step 3: If tests are approved, add the zero-token stop test**

Append this test to `ChatTurnOrchestratorTests`:

```csharp
[Fact]
public async Task RunTurnAsyncWhenStopRequestedBeforeTextMarksStoppedWithNullContent()
{
    (_, ChatMessage assistant, TurnRequested job) = SeedPendingTurn();
    _stopSignal.EnqueueResponse(true);

    FakeAgentRunner runner = new(ctx => FakeAgentRunner.Tokens(ctx.TurnId, ["ignored"]));

    await CreateOrchestrator(runner).RunTurnAsync(job, CancellationToken.None);

    Assert.Equal(MessageStatus.Stopped, assistant.Status);
    Assert.Null(assistant.Content);
    Assert.Equal(1, _unitOfWork.SaveCount);
    Assert.Empty(_publisher.Events.OfType<TokenEvent>());
    Assert.IsType<StoppedEvent>(_publisher.Events[^1]);
}
```

- [ ] **Step 4: If tests are approved, run the orchestrator tests and verify they fail**

Run with elevated permissions:

```bash
dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~ChatTurnOrchestratorTests"
```

Expected: FAIL because `ChatTurnOrchestrator` does not accept or use `ITurnStopSignal`.

- [ ] **Step 5: Inject `ITurnStopSignal` into the orchestrator**

Update the primary constructor in `src/services/Chat/Chat.Application/Turns/ChatTurnOrchestrator.cs`:

```csharp
IAgentRunner agentRunner,
ITurnStopSignal stopSignal,
IUnitOfWork unitOfWork,
```

- [ ] **Step 6: Check the stop signal before publishing each event**

Inside the `await foreach` loop, place this check before appending/publishing the current event:

```csharp
if (await stopSignal.IsStopRequestedAsync(assistantMessage.Id.Value, cancellationToken))
{
    await StopTurnAsync
    (
        thread: thread,
        assistantMessage: assistantMessage,
        text: text.ToString(),
        cancellationToken: cancellationToken
    );
    return;
}
```

Then leave the existing token append and publish logic after it:

```csharp
if (turnEvent is TokenEvent token)
{
    text.Append(token.Text);
}

await publisher.PublishAsync(turnEvent, cancellationToken);
```

This ordering ensures a stop request observed before the next provider event does not publish an extra token after the user hit stop.

- [ ] **Step 7: Add the stop helper**

Add this private method near `FailTurnAsync`:

```csharp
private async Task StopTurnAsync
(
    ChatThread thread,
    ChatMessage assistantMessage,
    string text,
    CancellationToken cancellationToken
)
{
    MessageContent? content = null;

    if (!string.IsNullOrWhiteSpace(text))
    {
        ErrorOr<MessageContent> contentResult = MessageContent.Create(text);

        if (contentResult.IsError)
        {
            await FailTurnAsync
            (
                thread: thread,
                assistantMessage: assistantMessage,
                reason: contentResult.FirstError.Description,
                cancellationToken: cancellationToken
            );
            return;
        }

        content = contentResult.Value;
    }

    ErrorOr<ChatMessage> stopResult = thread.StopAssistantMessage
    (
        messageId: assistantMessage.Id,
        content: content,
        stoppedAt: dateTimeProvider.UtcNow
    );

    if (stopResult.IsError)
    {
        LogTurnAlreadyTerminal(assistantMessage.Id.Value);
        return;
    }

    await unitOfWork.SaveChangesAsync(cancellationToken);
    await publisher.PublishAsync(new StoppedEvent(assistantMessage.Id.Value), cancellationToken);
}
```

- [ ] **Step 8: If tests are approved, run the orchestrator tests**

Run with elevated permissions:

```bash
dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~ChatTurnOrchestratorTests"
```

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/services/Chat/Chat.Application/Turns/ChatTurnOrchestrator.cs tests/Chat/Chat.Application.Tests/Turns/ChatTurnOrchestratorTests.cs
git commit -m "feat(chat): stop turns cooperatively"
```

If tests were not approved, omit the test path from `git add`.

---

## Task 7: Include Stopped Content In Future Context

**Files:**
- Modify: `src/services/Chat/Chat.Application/Turns/ContextBuilder.cs`
- Test with approval: `tests/Chat/Chat.Application.Tests/Turns/ContextBuilderTests.cs`

- [ ] **Step 1: If tests are approved, add a context-builder test for stopped assistant content**

Append this test to `tests/Chat/Chat.Application.Tests/Turns/ContextBuilderTests.cs`:

```csharp
[Fact]
public async Task BuildAsyncIncludesStoppedAssistantContentInHistory()
{
    LlmProvider provider = TestCatalogFactory.CreateProvider();
    LlmModel model = provider.AddModel
    (
        externalModelId: ExternalModelId.FromDatabase("gpt-4.1"),
        profile: TestCatalogFactory.CreateProfile()
    ).Value;
    _providers.AddExistingProvider(provider);

    ChatThread thread = ChatThread.Create
    (
        userId: UserId.Create("auth0|user-1").Value,
        title: ChatTitle.Create("Hello").Value,
        firstUserMessage: MessageContent.Create("Hello").Value,
        createdAt: Now
    );

    ChatMessage stoppedAssistant = thread.BeginAssistantMessage(thread.CurrentMessageId, model.Id, Now).Value;
    thread.StopAssistantMessage(stoppedAssistant.Id, MessageContent.Create("Partial answer").Value, Now);
    ChatMessage followUp = thread.AddUserMessage(stoppedAssistant.Id, MessageContent.Create("Continue").Value, Now).Value;
    ChatMessage nextAssistant = thread.BeginAssistantMessage(followUp.Id, model.Id, Now).Value;

    ContextBuilder builder = new(_providers);

    ErrorOr<TurnContext> result = await builder
        .BuildAsync(thread, nextAssistant, RetrievedMemories.Empty, TurnGenerationOptions.Default, CancellationToken.None);

    Assert.False(result.IsError);
    Assert.Collection
    (
        result.Value.Messages,
        message =>
        {
            Assert.Equal(TurnRole.User, message.Role);
            Assert.Equal("Hello", message.Text);
        },
        message =>
        {
            Assert.Equal(TurnRole.Assistant, message.Role);
            Assert.Equal("Partial answer", message.Text);
        },
        message =>
        {
            Assert.Equal(TurnRole.User, message.Role);
            Assert.Equal("Continue", message.Text);
        }
    );
}
```

- [ ] **Step 2: If tests are approved, run the focused context-builder test and verify it fails**

Run with elevated permissions:

```bash
dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~ContextBuilderTests"
```

Expected: FAIL because stopped content is not included in history yet.

- [ ] **Step 3: Add a local terminal-content helper**

In `src/services/Chat/Chat.Application/Turns/ContextBuilder.cs`, replace:

```csharp
if (message.Content is not null && message.Status == MessageStatus.Completed)
```

with:

```csharp
if (HasContextContent(message))
```

Add this private static helper at the bottom of the class:

```csharp
private static bool HasContextContent(ChatMessage message) =>
    message.Content is not null
    && message.Status is MessageStatus.Completed or MessageStatus.Stopped;
```

This does not change `IContextBuilder` inputs or responsibilities; it only updates which terminal assistant states are eligible for existing history assembly.

- [ ] **Step 4: If tests are approved, run the context-builder tests**

Run with elevated permissions:

```bash
dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~ContextBuilderTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/services/Chat/Chat.Application/Turns/ContextBuilder.cs tests/Chat/Chat.Application.Tests/Turns/ContextBuilderTests.cs
git commit -m "feat(chat): include stopped assistant content in context"
```

If tests were not approved, omit the test path from `git add`.

---

## Task 8: Full Verification

**Files:**
- No source edits expected.

- [ ] **Step 1: Run application tests if approved**

Run with elevated permissions:

```bash
dotnet test tests/Chat/Chat.Application.Tests
```

Expected: PASS.

- [ ] **Step 2: Run domain tests if approved**

Run with elevated permissions:

```bash
dotnet test tests/Chat/Chat.Domain.Tests
```

Expected: PASS.

- [ ] **Step 3: Run solution build**

Run with elevated permissions:

```bash
dotnet build Nova.slnx
```

Expected: PASS.

- [ ] **Step 4: Inspect git status**

```bash
git status --short
```

Expected: only intentionally unrelated pre-existing workspace changes remain. At the time this plan was written, `docs/superpowers/specs/2026-06-24-chat-search-design.md` was already staged and unrelated to stop generation.
