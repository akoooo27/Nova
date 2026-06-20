using Chat.Application.Chats.Commands.RegenerateMessage;
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

public sealed class RegenerateMessageHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);

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

        ChatMessage rootUser = Assert.Single(thread.Messages, message => message.Role == MessageRole.User);

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