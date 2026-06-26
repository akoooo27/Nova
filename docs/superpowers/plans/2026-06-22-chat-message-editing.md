# Chat Message Editing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a dedicated endpoint that edits an active-path user message by creating an immutable sibling branch and immediately starting a new assistant turn.

**Architecture:** Strengthen `ChatThread.EditUserMessage` with active-path and active-generation guards, then orchestrate it through a focused `Mediator` command/handler and FastEndpoints endpoint. Reuse the existing `BeginAssistantMessage`, `TurnRequested`, `TurnStartedResult`, model-usability checks, and MassTransit EF outbox; do not mutate old nodes or add persistence fields.

**Tech Stack:** .NET 10, C#, FastEndpoints, `Mediator.SourceGenerator` / `Mediator.Abstractions`, FluentValidation, ErrorOr, MassTransit EF outbox, EF Core, xUnit.

---

## Scope and File Map

The approved design is in `docs/superpowers/specs/2026-06-22-chat-message-editing-design.md`.

Files to modify:

- `src/services/Chat/Chat.Domain/Chats/ChatThread.cs` — enforce edit eligibility inside the aggregate.
- `src/services/Chat/Chat.Domain/Chats/ChatErrors.cs` — define inactive-path and generating-path edit conflicts.
- `tests/Chat/Chat.Domain.Tests/Chats/ChatThreadTests.cs` — cover the new guards and terminal failed-response behavior.
- `docs/diagrams/chat-thread-aggregate.md` — keep the aggregate guard diagram aligned with the implementation.

Files to create:

- `src/services/Chat/Chat.Application/Chats/Commands/EditMessage/EditMessageCommand.cs` — define the application request.
- `src/services/Chat/Chat.Application/Chats/Commands/EditMessage/EditMessageCommandValidator.cs` — validate IDs and text shape.
- `src/services/Chat/Chat.Application/Chats/Commands/EditMessage/EditMessageHandler.cs` — create the two new nodes, publish the turn, and save atomically.
- `src/services/Chat/Chat.Api/Endpoints/Chats/EditMessage/Endpoint.cs` — expose the dedicated FastEndpoints route.
- `tests/Chat/Chat.Application.Tests/Chats/Commands/EditMessageCommandValidatorTests.cs` — verify command validation.
- `tests/Chat/Chat.Application.Tests/Turns/EditMessageHandlerTests.cs` — verify orchestration and failure behavior.

No EF migration, repository change, title update, edit-provenance field, inactive-node hiding, or MassTransit version change is required.

All `dotnet` commands below require elevated permission before execution, per `AGENTS.md`.

### Task 1: Enforce Edit Eligibility in `ChatThread`

**Files:**
- Modify: `tests/Chat/Chat.Domain.Tests/Chats/ChatThreadTests.cs:299-374`
- Modify: `src/services/Chat/Chat.Domain/Chats/ChatErrors.cs:53-59`
- Modify: `src/services/Chat/Chat.Domain/Chats/ChatThread.cs:330-367`

- [ ] **Step 1: Add failing domain tests for inactive and generating paths**

Add these tests beside the existing `EditUserMessage` tests:

```csharp
[Fact]
public void EditUserMessagePreservesOriginalDescendants()
{
    ChatThread chat = TestChatFactory.CreateThread();
    ChatMessage assistant = CompleteAssistant(chat);
    ChatMessage original = AddUser
    (
        chat,
        assistant.Id,
        TestChatFactory.CreatedAt.AddMinutes(2),
        "Original"
    );
    ChatMessage descendant = CompleteAssistant
    (
        chat,
        original.Id,
        TestChatFactory.CreatedAt.AddMinutes(3)
    );

    ErrorOr<ChatMessage> result = chat.EditUserMessage
    (
        messageId: original.Id,
        content: TestChatFactory.CreateContent("Edited"),
        createdAt: TestChatFactory.CreatedAt.AddMinutes(5)
    );

    Assert.False(result.IsError);
    Assert.Same(original, chat.FindMessage(original.Id));
    Assert.Same(descendant, chat.FindMessage(descendant.Id));
    Assert.Equal(original.Id, descendant.ParentMessageId);
    Assert.Equal(original.ParentMessageId, result.Value.ParentMessageId);
}

[Fact]
public void EditUserMessageReturnsEditTargetNotOnActivePathForInactiveUserTarget()
{
    ChatThread chat = TestChatFactory.CreateThread();
    ChatMessage assistant = CompleteAssistant(chat);
    ChatMessage inactiveUser = AddUser
    (
        chat,
        assistant.Id,
        TestChatFactory.CreatedAt.AddMinutes(2),
        "Inactive"
    );
    _ = AddUser
    (
        chat,
        assistant.Id,
        TestChatFactory.CreatedAt.AddMinutes(3),
        "Active"
    );

    ErrorOr<ChatMessage> result = chat.EditUserMessage
    (
        messageId: inactiveUser.Id,
        content: TestChatFactory.CreateContent("Edited inactive"),
        createdAt: TestChatFactory.CreatedAt.AddMinutes(4)
    );

    AssertError(result, ErrorType.Conflict, "Chat.EditTargetNotOnActivePath");
}

[Fact]
public void EditUserMessageReturnsCannotEditWhileGeneratingWhenActivePathIsGenerating()
{
    ChatThread chat = TestChatFactory.CreateThread();
    ChatMessage root = TestChatFactory.RootMessage(chat);
    _ = BeginAssistant(chat);

    ErrorOr<ChatMessage> result = chat.EditUserMessage
    (
        messageId: root.Id,
        content: TestChatFactory.CreateContent("Edited while generating"),
        createdAt: TestChatFactory.CreatedAt.AddMinutes(2)
    );

    AssertError(result, ErrorType.Conflict, "Chat.CannotEditWhileGenerating");
}

[Fact]
public void EditUserMessageAllowsEditAfterActiveAssistantFailed()
{
    ChatThread chat = TestChatFactory.CreateThread();
    ChatMessage root = TestChatFactory.RootMessage(chat);
    ChatMessage assistant = BeginAssistant(chat);
    ErrorOr<ChatMessage> failure = chat.FailAssistantMessage
    (
        messageId: assistant.Id,
        reason: TestChatFactory.CreateFailureReason(),
        failedAt: TestChatFactory.CreatedAt.AddMinutes(2)
    );
    Assert.False(failure.IsError);

    ErrorOr<ChatMessage> result = chat.EditUserMessage
    (
        messageId: root.Id,
        content: TestChatFactory.CreateContent("Edited after failure"),
        createdAt: TestChatFactory.CreatedAt.AddMinutes(3)
    );

    Assert.False(result.IsError);
    Assert.Equal(result.Value.Id, chat.CurrentMessageId);
}

[Fact]
public void EditUserMessageAllowsEditingTemporaryChat()
{
    ChatThread chat = TestChatFactory.CreateThread(isTemporary: true);
    ChatMessage root = TestChatFactory.RootMessage(chat);

    ErrorOr<ChatMessage> result = chat.EditUserMessage
    (
        messageId: root.Id,
        content: TestChatFactory.CreateContent("Edited temporary chat"),
        createdAt: TestChatFactory.CreatedAt.AddMinutes(1)
    );

    Assert.False(result.IsError);
    Assert.True(chat.IsTemporary);
    Assert.Equal(result.Value.Id, chat.CurrentMessageId);
}
```

- [ ] **Step 2: Run the edit-domain tests and confirm the new guards fail**

Request elevated permission, then run:

```bash
dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj --filter "FullyQualifiedName~ChatThreadTests.EditUserMessage" --no-restore
```

Expected: FAIL because inactive targets and generating active paths are currently accepted. The existing edit tests and the failed-assistant test should pass.

- [ ] **Step 3: Add the two domain errors**

Insert after `EditTargetMustBeUser` in `ChatErrors.cs`:

```csharp
public static Error EditTargetNotOnActivePath(ChatMessageId messageId) =>
    Error.Conflict
    (
        code: "Chat.EditTargetNotOnActivePath",
        description: $"User message '{messageId.Value}' is not on the active conversation path and cannot be edited."
    );

public static Error CannotEditWhileGenerating(ChatMessageId messageId) =>
    Error.Conflict
    (
        code: "Chat.CannotEditWhileGenerating",
        description: $"Active-path assistant message '{messageId.Value}' is still generating; wait for the turn to finish before editing."
    );
```

- [ ] **Step 4: Add active-path guards to `EditUserMessage`**

After the existing role check and before creating the sibling, add:

```csharp
List<ChatMessage> activePath = [];
ChatMessage? cursor = FindMessage(CurrentMessageId);

while (cursor is not null)
{
    activePath.Add(cursor);
    cursor = cursor.ParentMessageId is { } parentMessageId
        ? FindMessage(parentMessageId)
        : null;
}

if (!activePath.Any(message => message.Id == target.Id))
{
    return ChatErrors.EditTargetNotOnActivePath(messageId);
}

ChatMessage? generatingAssistant = activePath.FirstOrDefault
(
    message => message.Role == MessageRole.Assistant && message.Status == MessageStatus.Generating
);

if (generatingAssistant is not null)
{
    return ChatErrors.CannotEditWhileGenerating(generatingAssistant.Id);
}
```

Replace the existing XML summary with:

```csharp
/// <summary>
/// Creates an edited sibling of an active-path user message under the same parent (a new branch),
/// leaving the original untouched. Editing a root user message creates another root sibling.
/// Editing is rejected while an assistant on the active path is still generating.
/// </summary>
```

Preserve the existing target-not-found then role-check ordering so assistant targets continue returning `Chat.EditTargetMustBeUser`.

- [ ] **Step 5: Run the focused domain tests**

Request elevated permission, then run:

```bash
dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj --filter "FullyQualifiedName~ChatThreadTests.EditUserMessage" --no-restore
```

Expected: PASS for all edit tests.

- [ ] **Step 6: Commit the domain behavior**

```bash
git add src/services/Chat/Chat.Domain/Chats/ChatThread.cs src/services/Chat/Chat.Domain/Chats/ChatErrors.cs tests/Chat/Chat.Domain.Tests/Chats/ChatThreadTests.cs
git commit -m "feat(chat): guard user message editing"
```

### Task 2: Define and Validate the Edit Command

**Files:**
- Create: `src/services/Chat/Chat.Application/Chats/Commands/EditMessage/EditMessageCommand.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/EditMessage/EditMessageCommandValidator.cs`
- Create: `tests/Chat/Chat.Application.Tests/Chats/Commands/EditMessageCommandValidatorTests.cs`

- [ ] **Step 1: Write the validator tests before the command exists**

Create `EditMessageCommandValidatorTests.cs`:

```csharp
using Chat.Application.Chats.Commands.EditMessage;
using Chat.Domain.Chats.ValueObjects;

using FluentValidation.Results;

namespace Chat.Application.Tests.Chats.Commands;

public sealed class EditMessageCommandValidatorTests
{
    private readonly EditMessageCommandValidator _validator = new();

    [Fact]
    public void ValidateAcceptsPopulatedCommand()
    {
        EditMessageCommand command = new
        (
            ChatId: Guid.CreateVersion7(),
            MessageId: Guid.CreateVersion7(),
            Message: "Edited text",
            LlmModelId: Guid.CreateVersion7()
        );

        ValidationResult result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("chat")]
    [InlineData("message")]
    [InlineData("model")]
    public void ValidateRejectsEmptyRequiredId(string emptyField)
    {
        EditMessageCommand command = new
        (
            ChatId: emptyField == "chat" ? Guid.Empty : Guid.CreateVersion7(),
            MessageId: emptyField == "message" ? Guid.Empty : Guid.CreateVersion7(),
            Message: "Edited text",
            LlmModelId: emptyField == "model" ? Guid.Empty : Guid.CreateVersion7()
        );

        ValidationResult result = _validator.Validate(command);

        string expectedProperty = emptyField switch
        {
            "chat" => nameof(EditMessageCommand.ChatId),
            "message" => nameof(EditMessageCommand.MessageId),
            _ => nameof(EditMessageCommand.LlmModelId)
        };
        Assert.Contains(result.Errors, failure => failure.PropertyName == expectedProperty);
    }

    [Fact]
    public void ValidateRejectsEmptyMessage()
    {
        EditMessageCommand command = new
        (
            ChatId: Guid.CreateVersion7(),
            MessageId: Guid.CreateVersion7(),
            Message: string.Empty,
            LlmModelId: Guid.CreateVersion7()
        );

        ValidationResult result = _validator.Validate(command);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(EditMessageCommand.Message));
    }

    [Fact]
    public void ValidateRejectsOversizedMessage()
    {
        EditMessageCommand command = new
        (
            ChatId: Guid.CreateVersion7(),
            MessageId: Guid.CreateVersion7(),
            Message: new string('x', MessageContent.MaxLength + 1),
            LlmModelId: Guid.CreateVersion7()
        );

        ValidationResult result = _validator.Validate(command);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(EditMessageCommand.Message));
    }
}
```

- [ ] **Step 2: Run the validator tests and verify they fail to compile**

Request elevated permission, then run:

```bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter "FullyQualifiedName~EditMessageCommandValidatorTests" --no-restore
```

Expected: FAIL because `EditMessageCommand` and `EditMessageCommandValidator` do not exist.

- [ ] **Step 3: Create the command contract**

Create `EditMessageCommand.cs`:

```csharp
using Chat.Application.Abstractions.Turns;
using Chat.Application.Chats.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.EditMessage;

public sealed record EditMessageCommand
(
    Guid ChatId,
    Guid MessageId,
    string Message,
    Guid LlmModelId,
    TurnGenerationOptions? GenerationOptions = null
) : ICommand<ErrorOr<TurnStartedResult>>;
```

- [ ] **Step 4: Create the FluentValidation validator**

Create `EditMessageCommandValidator.cs`:

```csharp
using Chat.Domain.Chats.ValueObjects;

using FluentValidation;

namespace Chat.Application.Chats.Commands.EditMessage;

internal sealed class EditMessageCommandValidator : AbstractValidator<EditMessageCommand>
{
    public EditMessageCommandValidator()
    {
        RuleFor(x => x.ChatId)
            .NotEmpty();

        RuleFor(x => x.MessageId)
            .NotEmpty();

        RuleFor(x => x.Message)
            .NotEmpty()
            .MaximumLength(MessageContent.MaxLength);

        RuleFor(x => x.LlmModelId)
            .NotEmpty();
    }
}
```

- [ ] **Step 5: Run the focused validator tests**

Request elevated permission, then run:

```bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter "FullyQualifiedName~EditMessageCommandValidatorTests" --no-restore
```

Expected: PASS.

- [ ] **Step 6: Commit the command and validator**

```bash
git add src/services/Chat/Chat.Application/Chats/Commands/EditMessage tests/Chat/Chat.Application.Tests/Chats/Commands/EditMessageCommandValidatorTests.cs
git commit -m "feat(chat): define edit message command"
```

### Task 3: Orchestrate the Edited Turn

**Files:**
- Create: `tests/Chat/Chat.Application.Tests/Turns/EditMessageHandlerTests.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/EditMessage/EditMessageHandler.cs`

- [ ] **Step 1: Write handler tests for the successful flow and generation options**

Create `EditMessageHandlerTests.cs` with the fixture and first two tests:

```csharp
using Chat.Application.Abstractions.Turns;
using Chat.Application.Chats.Commands.EditMessage;
using Chat.Application.Chats.Results;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Tests.ModelCatalog;
using Chat.Application.Tests.ModelCatalog.LlmProviders;
using Chat.Application.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Turns;

public sealed class EditMessageHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeChatRepository _chats = new();
    private readonly FakeLlmProviderRepository _providers = new();
    private readonly FakeMessageBus _messageBus = new();
    private readonly TurnFakeUnitOfWork _unitOfWork = new();

    private LlmModel SeedModel(Action<LlmProvider, LlmModel>? configure = null)
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        LlmModel model = provider.AddModel
        (
            externalModelId: TestCatalogFactory.CreateExternalModelId(),
            profile: TestCatalogFactory.CreateProfile()
        ).Value;

        configure?.Invoke(provider, model);
        _providers.AddExistingProvider(provider);

        return model;
    }

    private (ChatThread Thread, ChatMessage Root) SeedThreadWithCompletedTurn(LlmModel model)
    {
        ChatThread thread = ChatThread.Create
        (
            userId: UserId.Create("auth0|user-1").Value,
            title: ChatTitle.Create("Original title").Value,
            firstUserMessage: MessageContent.Create("Original message").Value,
            createdAt: Now
        );
        ChatMessage root = thread.FindMessage(thread.CurrentMessageId)!;
        ChatMessage assistant = thread.BeginAssistantMessage(root.Id, model.Id, Now).Value;
        _ = thread.CompleteAssistantMessage
        (
            assistant.Id,
            MessageContent.Create("Original response").Value,
            Now
        );
        _chats.Seed(thread);

        return (thread, root);
    }

    private EditMessageHandler CreateHandler() => new
    (
        userContext: new FakeUserContext("auth0|user-1"),
        chats: _chats,
        providers: _providers,
        bus: _messageBus,
        unitOfWork: _unitOfWork,
        dateTimeProvider: new FakeDateTimeProvider(Now)
    );

    [Fact]
    public async Task HandleCreatesEditedSiblingAndGeneratingAssistantAndPublishesTurn()
    {
        LlmModel model = SeedModel();
        (ChatThread thread, ChatMessage root) = SeedThreadWithCompletedTurn(model);

        ErrorOr<TurnStartedResult> result = await CreateHandler().Handle
        (
            new EditMessageCommand
            (
                thread.Id.Value,
                root.Id.Value,
                "Edited message",
                model.Id.Value
            ),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Equal(thread.Id.Value, result.Value.ChatId);
        Assert.Equal("Original title", thread.Title.Value);
        Assert.Equal("Original message", root.Content!.Value);

        ChatMessage edited = Assert.Single
        (
            thread.Messages,
            message => message.Id.Value == result.Value.UserMessageId
                       && message.Role == MessageRole.User
        );
        ChatMessage assistant = Assert.Single
        (
            thread.Messages,
            message => message.Id.Value == result.Value.AssistantMessageId
                       && message.Role == MessageRole.Assistant
                       && message.Status == MessageStatus.Generating
        );
        Assert.Null(edited.ParentMessageId);
        Assert.Equal("Edited message", edited.Content!.Value);
        Assert.Equal(edited.Id, assistant.ParentMessageId);
        Assert.Equal(model.Id, assistant.LlmModelId);
        Assert.Equal(assistant.Id, thread.CurrentMessageId);

        TurnRequested turn = Assert.IsType<TurnRequested>(Assert.Single(_messageBus.Published));
        Assert.Equal(thread.Id.Value, turn.ChatId);
        Assert.Equal("auth0|user-1", turn.UserId);
        Assert.Equal(assistant.Id.Value, turn.AssistantMessageId);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleForwardsForceUseSearch()
    {
        LlmModel model = SeedModel();
        (ChatThread thread, ChatMessage root) = SeedThreadWithCompletedTurn(model);

        ErrorOr<TurnStartedResult> result = await CreateHandler().Handle
        (
            new EditMessageCommand
            (
                ChatId: thread.Id.Value,
                MessageId: root.Id.Value,
                Message: "Edited message",
                LlmModelId: model.Id.Value,
                GenerationOptions: new TurnGenerationOptions(ForceUseSearch: true)
            ),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        TurnRequested turn = Assert.IsType<TurnRequested>(Assert.Single(_messageBus.Published));
        Assert.NotNull(turn.Options);
        Assert.True(turn.Options.ForceUseSearch);
    }
```

- [ ] **Step 2: Add handler failure tests to the same class**

Append these tests and close the class:

```csharp
    [Fact]
    public async Task HandleReturnsChatNotFoundWithoutPublishingOrSaving()
    {
        LlmModel model = SeedModel();

        ErrorOr<TurnStartedResult> result = await CreateHandler().Handle
        (
            new EditMessageCommand
            (
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                "Edited message",
                model.Id.Value
            ),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal("Chat.NotFound", result.FirstError.Code);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsMessageNotFoundWithoutPublishingOrSaving()
    {
        LlmModel model = SeedModel();
        (ChatThread thread, _) = SeedThreadWithCompletedTurn(model);

        ErrorOr<TurnStartedResult> result = await CreateHandler().Handle
        (
            new EditMessageCommand
            (
                thread.Id.Value,
                Guid.CreateVersion7(),
                "Edited message",
                model.Id.Value
            ),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal("Chat.MessageNotFound", result.FirstError.Code);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsInactivePathConflictWithoutPublishingOrSaving()
    {
        LlmModel model = SeedModel();
        (ChatThread thread, _) = SeedThreadWithCompletedTurn(model);
        ChatMessage completedAssistant = thread.FindMessage(thread.CurrentMessageId)!;
        ChatMessage inactiveUser = thread.AddUserMessage
        (
            completedAssistant.Id,
            MessageContent.Create("Inactive").Value,
            Now
        ).Value;
        _ = thread.AddUserMessage
        (
            completedAssistant.Id,
            MessageContent.Create("Active").Value,
            Now
        );

        ErrorOr<TurnStartedResult> result = await CreateHandler().Handle
        (
            new EditMessageCommand
            (
                thread.Id.Value,
                inactiveUser.Id.Value,
                "Edited inactive",
                model.Id.Value
            ),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal("Chat.EditTargetNotOnActivePath", result.FirstError.Code);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsGeneratingConflictWithoutPublishingOrSaving()
    {
        LlmModel model = SeedModel();
        ChatThread thread = ChatThread.Create
        (
            userId: UserId.Create("auth0|user-1").Value,
            title: ChatTitle.Create("Original title").Value,
            firstUserMessage: MessageContent.Create("Original message").Value,
            createdAt: Now
        );
        ChatMessage root = thread.FindMessage(thread.CurrentMessageId)!;
        _ = thread.BeginAssistantMessage(root.Id, model.Id, Now);
        _chats.Seed(thread);

        ErrorOr<TurnStartedResult> result = await CreateHandler().Handle
        (
            new EditMessageCommand
            (
                thread.Id.Value,
                root.Id.Value,
                "Edited message",
                model.Id.Value
            ),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal("Chat.CannotEditWhileGenerating", result.FirstError.Code);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsModelNotFoundWithoutChangingTheThread()
    {
        LlmModel existingModel = SeedModel();
        (ChatThread thread, ChatMessage root) = SeedThreadWithCompletedTurn(existingModel);
        int originalMessageCount = thread.Messages.Count;

        ErrorOr<TurnStartedResult> result = await CreateHandler().Handle
        (
            new EditMessageCommand
            (
                thread.Id.Value,
                root.Id.Value,
                "Edited message",
                Guid.CreateVersion7()
            ),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal("Chat.LlmModelNotFound", result.FirstError.Code);
        Assert.Equal(originalMessageCount, thread.Messages.Count);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsDisabledModelWithoutChangingTheThread()
    {
        LlmModel disabledModel = SeedModel
        (
            (provider, model) => provider.DisableModel(model.Id)
        );
        (ChatThread thread, ChatMessage root) = SeedThreadWithCompletedTurn(disabledModel);
        int originalMessageCount = thread.Messages.Count;

        ErrorOr<TurnStartedResult> result = await CreateHandler().Handle
        (
            new EditMessageCommand
            (
                thread.Id.Value,
                root.Id.Value,
                "Edited message",
                disabledModel.Id.Value
            ),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal("Chat.LlmModelDisabled", result.FirstError.Code);
        Assert.Equal(originalMessageCount, thread.Messages.Count);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }
}
```

- [ ] **Step 3: Run the handler tests and verify they fail**

Request elevated permission, then run:

```bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter "FullyQualifiedName~EditMessageHandlerTests" --no-restore
```

Expected: FAIL because `EditMessageHandler` does not exist.

- [ ] **Step 4: Implement `EditMessageHandler`**

Create `EditMessageHandler.cs`:

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

using SharedKernel;

namespace Chat.Application.Chats.Commands.EditMessage;

internal sealed class EditMessageHandler(
    IUserContext userContext,
    IChatRepository chats,
    ILlmProviderRepository providers,
    IMessageBus bus,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<EditMessageCommand, ErrorOr<TurnStartedResult>>
{
    public async ValueTask<ErrorOr<TurnStartedResult>> Handle
    (
        EditMessageCommand command,
        CancellationToken cancellationToken
    )
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(command.ChatId);
        ErrorOr<ChatMessageId> messageIdResult = ChatMessageId.Create(command.MessageId);
        ErrorOr<MessageContent> contentResult = MessageContent.Create(command.Message);
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

        if (messageIdResult.IsError)
        {
            errors.AddRange(messageIdResult.Errors);
        }

        if (contentResult.IsError)
        {
            errors.AddRange(contentResult.Errors);
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
        ChatMessageId messageId = messageIdResult.Value;
        MessageContent content = contentResult.Value;
        LlmModelId modelId = modelIdResult.Value;
        TurnGenerationOptions generationOptions = command.GenerationOptions ?? TurnGenerationOptions.Default;

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

        ChatThread? thread = await chats.GetByIdAsync(chatId, userId, cancellationToken);

        if (thread is null)
        {
            return ChatOperationErrors.ChatNotFound(chatId);
        }

        DateTimeOffset now = dateTimeProvider.UtcNow;

        ErrorOr<ChatMessage> editedResult = thread.EditUserMessage(messageId, content, now);

        if (editedResult.IsError)
        {
            return editedResult.Errors;
        }

        ChatMessageId editedMessageId = editedResult.Value.Id;
        ErrorOr<ChatMessage> assistantResult = thread.BeginAssistantMessage
        (
            parentMessageId: editedMessageId,
            llmModelId: modelId,
            createdAt: now
        );

        if (assistantResult.IsError)
        {
            return assistantResult.Errors;
        }

        ChatMessageId assistantMessageId = assistantResult.Value.Id;
        TurnRequested turnRequested = new
        (
            ChatId: thread.Id.Value,
            UserId: userId.Value,
            AssistantMessageId: assistantMessageId.Value,
            Options: generationOptions
        );

        // Published BEFORE SaveChangesAsync on purpose: the MassTransit bus outbox buffers
        // this and writes it to the outbox table inside the same transaction (spec: no dual-write).
        await bus.PublishAsync(turnRequested, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new TurnStartedResult
        (
            ChatId: thread.Id.Value,
            UserMessageId: editedMessageId.Value,
            AssistantMessageId: assistantMessageId.Value
        );
    }
}
```

- [ ] **Step 5: Run the focused handler tests**

Request elevated permission, then run:

```bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter "FullyQualifiedName~EditMessageHandlerTests" --no-restore
```

Expected: PASS.

- [ ] **Step 6: Commit the handler orchestration**

```bash
git add src/services/Chat/Chat.Application/Chats/Commands/EditMessage/EditMessageHandler.cs tests/Chat/Chat.Application.Tests/Turns/EditMessageHandlerTests.cs
git commit -m "feat(chat): orchestrate edited message turns"
```

### Task 4: Expose the FastEndpoints Route

**Files:**
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/EditMessage/Endpoint.cs`

- [ ] **Step 1: Create the endpoint**

Create `Endpoint.cs`:

```csharp
using Chat.Api.Endpoints.Chats.Responses;
using Chat.Application.Abstractions.Turns;
using Chat.Application.Chats.Commands.EditMessage;
using Chat.Application.Chats.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.EditMessage;

internal sealed record Request
(
    string Message,
    Guid ModelId,
    bool ForceUseSearch = false
);

internal sealed class Endpoint(ISender sender) : Endpoint<Request>
{
    public const string RouteName = "Chat.Chats.EditMessage";

    public override void Configure()
    {
        Post("/chats/{chatId}/messages/{messageId}/edit");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Edit Message")
                .WithDescription("Creates an edited sibling of an active-path user message and starts generating a new assistant reply asynchronously.")
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
        EditMessageCommand command = new
        (
            ChatId: Route<Guid>("chatId"),
            MessageId: Route<Guid>("messageId"),
            Message: request.Message,
            LlmModelId: request.ModelId,
            GenerationOptions: new TurnGenerationOptions(ForceUseSearch: request.ForceUseSearch)
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

- [ ] **Step 2: Build the Chat API to verify source generation and endpoint discovery**

Request elevated permission, then run:

```bash
dotnet build src/services/Chat/Chat.Api/Chat.Api.csproj --no-restore
```

Expected: build succeeds with 0 errors. This verifies the `Mediator` source-generated handler registration and FastEndpoints endpoint compile together.

- [ ] **Step 3: Commit the endpoint**

```bash
git add src/services/Chat/Chat.Api/Endpoints/Chats/EditMessage/Endpoint.cs
git commit -m "feat(chat): expose message editing endpoint"
```

### Task 5: Align the Aggregate Documentation

**Files:**
- Modify: `docs/diagrams/chat-thread-aggregate.md:65-86`

- [ ] **Step 1: Update the edit guard diagram and explanation**

Change the `EUM` node in the guard diagram to:

```mermaid
EUM["EditUserMessage<br/>target must be an active-path User;<br/>active path must not be Generating;<br/>sibling under target's parent"]
```

After the strict-alternation paragraph, add:

```markdown
Editing is restricted to user nodes on the current root-to-head path. If that path contains a
`Generating` assistant, editing is rejected until the turn reaches `Completed` or `Failed`.
Temporary chats use the same edit behavior because they share the same aggregate model.
```

- [ ] **Step 2: Check the documentation diff**

Run:

```bash
git diff --check
git diff -- docs/diagrams/chat-thread-aggregate.md
```

Expected: no whitespace errors; the diagram and prose describe active-path-only editing and the generation guard.

- [ ] **Step 3: Commit the documentation update**

```bash
git add docs/diagrams/chat-thread-aggregate.md
git commit -m "docs(chat): document message edit guards"
```

### Task 6: Run Full Verification

**Files:**
- Verify only; no planned file changes.

- [ ] **Step 1: Run the complete domain test project**

Request elevated permission, then run:

```bash
dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj --no-restore
```

Expected: PASS with 0 failed tests.

- [ ] **Step 2: Run the complete application test project**

Request elevated permission, then run:

```bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --no-restore
```

Expected: PASS with 0 failed tests.

- [ ] **Step 3: Build the full solution**

Request elevated permission, then run:

```bash
dotnet build Nova.slnx --no-restore
```

Expected: build succeeds with 0 errors.

- [ ] **Step 4: Verify the final diff and repository state**

Run:

```bash
git diff --check
git status --short
git log -6 --oneline
```

Expected: no uncommitted implementation changes, no diff-check errors, and the task commits appear in order.

## Completion Criteria

- `POST /v1/chats/{chatId}/messages/{messageId}/edit` accepts only `message`, required `modelId`, and optional `forceUseSearch`.
- Only active-path user messages are editable.
- Editing is blocked while the active path contains a generating assistant.
- Root, non-root, completed-response, failed-response, and temporary-chat edits use the same aggregate behavior.
- The original message, descendants, and chat title remain unchanged.
- The server creates the edited user ID and assistant ID, moves the head naturally, and returns the existing `TurnStartedResponse` including its stream path.
- Exactly one `TurnRequested` is published through the existing outbox flow.
- Domain and application tests pass, and the solution builds without a migration or package change.
