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
        LlmModel disabledModel = SeedModel((provider, model) => provider.DisableModel(model.Id));
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