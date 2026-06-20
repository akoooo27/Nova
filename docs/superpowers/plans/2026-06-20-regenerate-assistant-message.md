# Regenerate Assistant Message Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an endpoint that regenerates an assistant message as a new sibling under the same user message, optionally with a different model, reusing the existing async turn pipeline.

**Architecture:** Pure wiring over existing machinery. A new `RegenerateMessageCommand` + handler call the existing `ChatThread.RegenerateAssistant` domain method and publish the existing `TurnRequested` event; a FastEndpoints endpoint exposes it. No domain change, no EF migration, no change to the turn orchestrator or context builder.

**Tech Stack:** C# / .NET 10, DDD + CQRS, `Mediator` source-generated dispatch, FluentValidation (command-level via a `ValidationBehavior` pipeline), `ErrorOr` for error flows, FastEndpoints for HTTP, xUnit for tests.

## Global Constraints

- Target framework `net10.0`; the chat service uses C# file-scoped namespaces and the existing formatting style (opening brace on its own line, vertically-aligned argument lists).
- Use the existing `Mediator` package APIs — **never** introduce MediatR.
- Do not use ASP.NET Core controllers; HTTP is FastEndpoints only.
- Errors flow through `ErrorOr`; endpoints surface them via `CustomResults.Problem(result)`.
- Publish domain/integration events **before** `IUnitOfWork.SaveChangesAsync` (the MassTransit bus outbox buffers into the same transaction — no dual-write).
- Commit messages follow `type(scope): summary` (e.g. `feat(chat): ...`). **Do not add a `Co-Authored-By` trailer.**
- Read `IDateTimeProvider.UtcNow` once per handler execution.
- Test project: `tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj`.

---

## File Structure

**Create (production):**
- `src/services/Chat/Chat.Application/Chats/Commands/RegenerateMessage/RegenerateMessageCommand.cs` — the command record.
- `src/services/Chat/Chat.Application/Chats/Commands/RegenerateMessage/RegenerateMessageCommandValidator.cs` — FluentValidation rules.
- `src/services/Chat/Chat.Application/Chats/Commands/RegenerateMessage/RegenerateMessageHandler.cs` — load thread, resolve model, regenerate, publish, save.
- `src/services/Chat/Chat.Api/Endpoints/Chats/RegenerateMessage/Endpoint.cs` — `POST /chats/{chatId}/messages/{messageId}/regenerate`.

**Create (tests):**
- `tests/Chat/Chat.Application.Tests/Turns/RegenerateMessageHandlerTests.cs`
- `tests/Chat/Chat.Application.Tests/Chats/Commands/RegenerateMessageCommandValidatorTests.cs`

**Reuse unchanged:** `ChatThread.RegenerateAssistant`, `TurnRequested`, `TurnStartedResult`, `TurnStartedResponse`, `ModelUsability`, `ChatOperationErrors`, `ChatErrors`, and the existing `tests/Chat/Chat.Application.Tests/Turns` fakes (`FakeChatRepository`, `FakeLlmProviderRepository`, `FakeMessageBus`, `TurnFakeUnitOfWork`, `FakeUserContext`, `FakeDateTimeProvider`, `TestCatalogFactory`).

---

## Task 1: Command + Validator

**Files:**
- Create: `src/services/Chat/Chat.Application/Chats/Commands/RegenerateMessage/RegenerateMessageCommand.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/RegenerateMessage/RegenerateMessageCommandValidator.cs`
- Test: `tests/Chat/Chat.Application.Tests/Chats/Commands/RegenerateMessageCommandValidatorTests.cs`

**Interfaces:**
- Consumes: `TurnStartedResult` (existing, `Chat.Application.Chats.Results`), `Mediator.ICommand<T>`.
- Produces: `RegenerateMessageCommand(Guid ChatId, Guid MessageId, Guid? ModelId = null, bool ForceUseSearch = false) : ICommand<ErrorOr<TurnStartedResult>>` and `RegenerateMessageCommandValidator`. Later tasks rely on these exact property names.

- [ ] **Step 1: Create the command record**

Create `RegenerateMessageCommand.cs`:

```csharp
using Chat.Application.Chats.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.RegenerateMessage;

public sealed record RegenerateMessageCommand
(
    Guid ChatId,
    Guid MessageId,
    Guid? ModelId = null,
    bool ForceUseSearch = false
) : ICommand<ErrorOr<TurnStartedResult>>;
```

- [ ] **Step 2: Create the validator**

Create `RegenerateMessageCommandValidator.cs`:

```csharp
using FluentValidation;

namespace Chat.Application.Chats.Commands.RegenerateMessage;

internal sealed class RegenerateMessageCommandValidator : AbstractValidator<RegenerateMessageCommand>
{
    public RegenerateMessageCommandValidator()
    {
        RuleFor(x => x.ChatId)
            .NotEmpty();

        RuleFor(x => x.MessageId)
            .NotEmpty();

        RuleFor(x => x.ModelId)
            .NotEqual(Guid.Empty)
            .When(x => x.ModelId.HasValue);
    }
}
```

- [ ] **Step 3: Write the failing validator tests**

Create `tests/Chat/Chat.Application.Tests/Chats/Commands/RegenerateMessageCommandValidatorTests.cs`:

```csharp
using Chat.Application.Chats.Commands.RegenerateMessage;

using FluentValidation.TestHelper;

namespace Chat.Application.Tests.Chats.Commands;

public sealed class RegenerateMessageCommandValidatorTests
{
    private readonly RegenerateMessageCommandValidator _validator = new();

    [Fact]
    public void PassesWhenChatIdAndMessageIdPresentAndModelIdOmitted()
    {
        RegenerateMessageCommand command = new(Guid.CreateVersion7(), Guid.CreateVersion7());

        TestValidationResult<RegenerateMessageCommand> result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void PassesWhenModelIdSupplied()
    {
        RegenerateMessageCommand command = new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        TestValidationResult<RegenerateMessageCommand> result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void FailsWhenChatIdEmpty()
    {
        RegenerateMessageCommand command = new(Guid.Empty, Guid.CreateVersion7());

        TestValidationResult<RegenerateMessageCommand> result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.ChatId);
    }

    [Fact]
    public void FailsWhenMessageIdEmpty()
    {
        RegenerateMessageCommand command = new(Guid.CreateVersion7(), Guid.Empty);

        TestValidationResult<RegenerateMessageCommand> result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.MessageId);
    }

    [Fact]
    public void FailsWhenModelIdSuppliedButEmpty()
    {
        RegenerateMessageCommand command = new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.Empty);

        TestValidationResult<RegenerateMessageCommand> result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.ModelId);
    }
}
```

- [ ] **Step 4: Run the validator tests**

Run: `dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter "FullyQualifiedName~RegenerateMessageCommandValidatorTests"`
Expected: PASS (5 tests). If `FluentValidation.TestHelper` is missing a using, confirm the test project already references FluentValidation (it does — other validator tests use `TestValidate`).

- [ ] **Step 5: Commit**

```bash
git add src/services/Chat/Chat.Application/Chats/Commands/RegenerateMessage tests/Chat/Chat.Application.Tests/Chats/Commands/RegenerateMessageCommandValidatorTests.cs
git commit -m "feat(chat): add regenerate message command and validator"
```

---

## Task 2: Handler

**Files:**
- Create: `src/services/Chat/Chat.Application/Chats/Commands/RegenerateMessage/RegenerateMessageHandler.cs`
- Test: `tests/Chat/Chat.Application.Tests/Turns/RegenerateMessageHandlerTests.cs`

**Interfaces:**
- Consumes: `RegenerateMessageCommand` (Task 1); `IUserContext`, `IChatRepository`, `ILlmProviderRepository`, `IMessageBus`, `IUnitOfWork`, `IDateTimeProvider` (existing); `ChatThread.RegenerateAssistant`, `TurnRequested`, `TurnStartedResult`, `ModelUsability.EnsureUsableAsync`, `ChatOperationErrors.ChatNotFound`, `ChatErrors.MessageNotFound` (existing).
- Produces: `RegenerateMessageHandler : ICommandHandler<RegenerateMessageCommand, ErrorOr<TurnStartedResult>>`.

- [ ] **Step 1: Write the failing handler tests**

Create `tests/Chat/Chat.Application.Tests/Turns/RegenerateMessageHandlerTests.cs`. This mirrors `SendMessageHandlerTests` setup (same fakes, same seeding helpers):

```csharp
using Chat.Application.Chats.Commands.RegenerateMessage;
using Chat.Application.Chats.Results;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Tests.ModelCatalog;
using Chat.Application.Tests.ModelCatalog.LlmProviders;
using Chat.Application.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Turns;

public sealed class RegenerateMessageHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeChatRepository _chats = new();
    private readonly FakeLlmProviderRepository _providers = new();
    private readonly FakeMessageBus _messageBus = new();
    private readonly TurnFakeUnitOfWork _unitOfWork = new();

    private LlmModel SeedModel(Action<Chat.Domain.ModelCatalog.LlmProvider, LlmModel>? configure = null)
    {
        Chat.Domain.ModelCatalog.LlmProvider provider = TestCatalogFactory.CreateProvider();
        LlmModel model = provider.AddModel
        (
            externalModelId: TestCatalogFactory.CreateExternalModelId(),
            profile: TestCatalogFactory.CreateProfile()
        ).Value;

        configure?.Invoke(provider, model);
        _providers.AddExistingProvider(provider);

        return model;
    }

    // Seeds: user "Hello" -> assistant (completed). Returns the completed assistant message.
    private (ChatThread Thread, ChatMessage Assistant) SeedThreadWithCompletedTurn(LlmModel model)
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

        return (thread, assistant);
    }

    private RegenerateMessageHandler CreateHandler() => new
    (
        userContext: new FakeUserContext("auth0|user-1"),
        chats: _chats,
        providers: _providers,
        bus: _messageBus,
        unitOfWork: _unitOfWork,
        dateTimeProvider: new FakeDateTimeProvider(Now)
    );

    [Fact]
    public async Task RegeneratesAsSiblingReusingOriginalModelWhenNoModelSupplied()
    {
        LlmModel model = SeedModel();
        (ChatThread thread, ChatMessage assistant) = SeedThreadWithCompletedTurn(model);

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new RegenerateMessageCommand(thread.Id.Value, assistant.Id.Value), CancellationToken.None);

        Assert.False(result.IsError);

        ChatMessage sibling = Assert.Single
        (
            thread.Messages,
            message => message.Id.Value == result.Value.AssistantMessageId
                       && message.Role == MessageRole.Assistant
                       && message.Status == MessageStatus.Generating
        );
        Assert.Equal(assistant.ParentMessageId, sibling.ParentMessageId);
        Assert.Equal(model.Id, sibling.LlmModelId);
        Assert.Equal(sibling.Id, thread.CurrentMessageId);
        Assert.Equal(assistant.ParentMessageId!.Value, result.Value.UserMessageId);

        TurnRequested published = Assert.IsType<TurnRequested>(Assert.Single(_messageBus.Published));
        Assert.Equal(result.Value.AssistantMessageId, published.AssistantMessageId);
        Assert.Equal("auth0|user-1", published.UserId);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task RegeneratesWithOverrideModelWhenModelSupplied()
    {
        LlmModel original = SeedModel();
        LlmModel replacement = SeedModel();
        (ChatThread thread, ChatMessage assistant) = SeedThreadWithCompletedTurn(original);

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new RegenerateMessageCommand(thread.Id.Value, assistant.Id.Value, replacement.Id.Value), CancellationToken.None);

        Assert.False(result.IsError);

        ChatMessage sibling = Assert.Single
        (
            thread.Messages,
            message => message.Id.Value == result.Value.AssistantMessageId
        );
        Assert.Equal(replacement.Id, sibling.LlmModelId);
    }

    [Fact]
    public async Task ForwardsForceUseSearchOption()
    {
        LlmModel model = SeedModel();
        (ChatThread thread, ChatMessage assistant) = SeedThreadWithCompletedTurn(model);

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new RegenerateMessageCommand(thread.Id.Value, assistant.Id.Value, ForceUseSearch: true), CancellationToken.None);

        Assert.False(result.IsError);

        TurnRequested published = Assert.IsType<TurnRequested>(Assert.Single(_messageBus.Published));
        Assert.NotNull(published.Options);
        Assert.True(published.Options.ForceUseSearch);
    }

    [Fact]
    public async Task ReturnsChatNotFoundWhenChatUnknown()
    {
        LlmModel model = SeedModel();
        (_, ChatMessage assistant) = SeedThreadWithCompletedTurn(model);

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new RegenerateMessageCommand(Guid.CreateVersion7(), assistant.Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.NotFound", result.FirstError.Code);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task ReturnsMessageNotFoundWhenTargetUnknown()
    {
        LlmModel model = SeedModel();
        (ChatThread thread, _) = SeedThreadWithCompletedTurn(model);

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new RegenerateMessageCommand(thread.Id.Value, Guid.CreateVersion7()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.MessageNotFound", result.FirstError.Code);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task ReturnsErrorWhenTargetIsUserMessage()
    {
        LlmModel model = SeedModel();
        (ChatThread thread, _) = SeedThreadWithCompletedTurn(model);

        // The root user message id is the parent of the seeded assistant.
        ChatMessage rootUser = Assert.Single(thread.Messages, m => m.Role == MessageRole.User);

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new RegenerateMessageCommand(thread.Id.Value, rootUser.Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.RegenerationTargetMustBeAssistant", result.FirstError.Code);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task ReturnsErrorWhenTargetStillGenerating()
    {
        LlmModel model = SeedModel();

        ChatThread thread = ChatThread.Create
        (
            userId: UserId.Create("auth0|user-1").Value,
            title: ChatTitle.Create("Hello").Value,
            firstUserMessage: MessageContent.Create("Hello").Value,
            createdAt: Now
        );
        ChatMessage assistant = thread.BeginAssistantMessage(thread.CurrentMessageId, model.Id, Now).Value;
        _chats.Seed(thread);

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new RegenerateMessageCommand(thread.Id.Value, assistant.Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.CannotRegenerateWhileGenerating", result.FirstError.Code);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task ReturnsModelNotFoundWhenOverrideModelUnknown()
    {
        LlmModel model = SeedModel();
        (ChatThread thread, ChatMessage assistant) = SeedThreadWithCompletedTurn(model);

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new RegenerateMessageCommand(thread.Id.Value, assistant.Id.Value, Guid.CreateVersion7()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.LlmModelNotFound", result.FirstError.Code);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter "FullyQualifiedName~RegenerateMessageHandlerTests"`
Expected: FAIL to compile — `RegenerateMessageHandler` does not exist yet.

- [ ] **Step 3: Write the handler**

Create `RegenerateMessageHandler.cs`. The model resolution rule: use the override `ModelId` when supplied, otherwise the target assistant's existing `LlmModelId` (assistant messages always have one).

```csharp
using Chat.Application.Abstractions.Database;
using Chat.Application.Abstractions.Turns;
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

namespace Chat.Application.Chats.Commands.RegenerateMessage;

internal sealed class RegenerateMessageHandler(
    IUserContext userContext,
    IChatRepository chats,
    ILlmProviderRepository providers,
    IMessageBus bus,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<RegenerateMessageCommand, ErrorOr<TurnStartedResult>>
{
    public async ValueTask<ErrorOr<TurnStartedResult>> Handle(RegenerateMessageCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(command.ChatId);
        ErrorOr<ChatMessageId> messageIdResult = ChatMessageId.Create(command.MessageId);

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
        TurnGenerationOptions generationOptions = new(ForceUseSearch: command.ForceUseSearch);

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

        ErrorOr<LlmModelId> modelIdResult = command.ModelId is { } overrideModelId
            ? LlmModelId.Create(overrideModelId)
            : ResolveTargetModel(target);

        if (modelIdResult.IsError)
        {
            return modelIdResult.Errors;
        }

        LlmModelId modelId = modelIdResult.Value;

        ErrorOr<Success> usabilityResult = await ModelUsability.EnsureUsableAsync
        (
            providers: providers,
            modelId: modelId,
            cancellationToken: cancellationToken,
            requiresToolCalling: generationOptions.ForceUseSearch
        );

        if (usabilityResult.IsError)
        {
            return usabilityResult.Errors;
        }

        DateTimeOffset now = dateTimeProvider.UtcNow;

        ErrorOr<ChatMessage> siblingResult = thread.RegenerateAssistant
        (
            messageId: messageId,
            llmModelId: modelId,
            createdAt: now
        );

        if (siblingResult.IsError)
        {
            return siblingResult.Errors;
        }

        ChatMessage sibling = siblingResult.Value;

        TurnRequested turnRequested = new
        (
            ChatId: thread.Id.Value,
            UserId: userId.Value,
            AssistantMessageId: sibling.Id.Value,
            Options: generationOptions
        );

        // Published BEFORE SaveChangesAsync on purpose: the MassTransit bus outbox buffers
        // this and writes it to the outbox table inside the same transaction (spec: no dual-write).
        await bus.PublishAsync(turnRequested, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new TurnStartedResult
        (
            ChatId: thread.Id.Value,
            UserMessageId: sibling.ParentMessageId!.Value,
            AssistantMessageId: sibling.Id.Value
        );
    }

    private static ErrorOr<LlmModelId> ResolveTargetModel(ChatMessage target) =>
        target.LlmModelId is { } existing
            ? existing
            : ChatErrors.RegenerationTargetMustBeAssistant(target.Id);
}
```

Note: `sibling.ParentMessageId` is guaranteed non-null — `RegenerateAssistant` only succeeds for an assistant message that has a parent, and copies that parent onto the new sibling. The `ResolveTargetModel` fallback returns `RegenerationTargetMustBeAssistant` for the impossible "assistant with no model" case so the handler never dereferences a null model id; in practice `RegenerateAssistant` would reject a non-assistant target first.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter "FullyQualifiedName~RegenerateMessageHandlerTests"`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add src/services/Chat/Chat.Application/Chats/Commands/RegenerateMessage/RegenerateMessageHandler.cs tests/Chat/Chat.Application.Tests/Turns/RegenerateMessageHandlerTests.cs
git commit -m "feat(chat): add regenerate message handler"
```

---

## Task 3: API Endpoint

**Files:**
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/RegenerateMessage/Endpoint.cs`

**Interfaces:**
- Consumes: `RegenerateMessageCommand` (Task 1), `TurnStartedResult`/`TurnStartedResponse` (existing), `ISender`, `CustomResults.Problem`, `CustomTags.Chats`.
- Produces: HTTP `POST /chats/{chatId}/messages/{messageId}/regenerate`.

- [ ] **Step 1: Create the endpoint**

Create `Endpoint.cs`, mirroring `Chat.Api/Endpoints/Chats/SendMessage/Endpoint.cs` (note `ModelId` is nullable here, and there are two route parameters):

```csharp
using Chat.Api.Endpoints.Chats.Responses;
using Chat.Application.Chats.Commands.RegenerateMessage;
using Chat.Application.Chats.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.RegenerateMessage;

internal sealed record Request
(
    Guid? ModelId = null,
    bool ForceUseSearch = false
);

internal sealed class Endpoint(ISender sender) : Endpoint<Request>
{
    public const string RouteName = "Chat.Chats.RegenerateMessage";

    public override void Configure()
    {
        Post("/chats/{chatId}/messages/{messageId}/regenerate");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Regenerate Message")
                .WithDescription("Regenerates an assistant message as a new sibling under the same user message, optionally with a different model, and starts generating asynchronously.")
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
        RegenerateMessageCommand command = new
        (
            ChatId: Route<Guid>("chatId"),
            MessageId: Route<Guid>("messageId"),
            ModelId: request.ModelId,
            ForceUseSearch: request.ForceUseSearch
        );

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

- [ ] **Step 2: Build the API project**

Run: `dotnet build src/services/Chat/Chat.Api/Chat.Api.csproj`
Expected: Build succeeded, 0 errors. (FastEndpoints discovers the endpoint by assembly scanning; the command handler and validator are auto-registered via the existing `Mediator` source generator and `AddValidatorsFromAssembly` in `Chat.Application/DependencyInjection.cs` — no manual registration.)

- [ ] **Step 3: Run the full chat test suite to confirm no regressions**

Run: `dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj`
Expected: PASS (all existing tests plus the 13 new ones).

- [ ] **Step 4: Commit**

```bash
git add src/services/Chat/Chat.Api/Endpoints/Chats/RegenerateMessage/Endpoint.cs
git commit -m "feat(chat): add regenerate message endpoint"
```

---

## Manual Verification (optional, after Task 3)

With the Chat API running and an existing chat that has at least one completed assistant message:

```bash
# Regenerate reusing the original model:
curl -i -X POST "http://localhost:<port>/v1/chats/<chatId>/messages/<assistantMessageId>/regenerate" \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" -d '{}'

# Regenerate with a different model:
curl -i -X POST "http://localhost:<port>/v1/chats/<chatId>/messages/<assistantMessageId>/regenerate" \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{ "modelId": "<otherModelId>" }'
```

Expected: `202 Accepted` with a `TurnStartedResponse` whose `assistantMessageId` is a new id and whose `streamPath` can be subscribed to. A follow-up `GET /chats/{chatId}` shows the original user message now has two assistant children, with `currentMessageId` pointing at the new one.

---

## Self-Review Notes

- **Spec coverage:** §1 scope → Tasks 1–3; §3 domain (no change) → reused in Task 2; §4 command/validator/handler/result → Tasks 1–2; §5 pipeline (no change) → relied on, manual verification; §6 API → Task 3; §8 error handling → handler returns + endpoint `CustomResults.Problem`, covered by handler tests for `ChatNotFound`, `MessageNotFound`, `RegenerationTargetMustBeAssistant`, `CannotRegenerateWhileGenerating`, `LlmModelNotFound`; §9 testing → validator + handler tests.
- **No migration / no domain edits** confirmed: only four new production files, two new test files.
- **Type consistency:** `RegenerateMessageCommand(ChatId, MessageId, ModelId?, ForceUseSearch)` used identically across command, validator, handler, tests, and endpoint; result is the existing `TurnStartedResult(ChatId, UserMessageId, AssistantMessageId)`.
