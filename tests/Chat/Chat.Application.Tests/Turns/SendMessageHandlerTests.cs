using Chat.Application.Chats.Commands.SendMessage;
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

public sealed class SendMessageHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

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
        providers: _providers,
        bus: _messageBus,
        unitOfWork: _unitOfWork,
        dateTimeProvider: new FakeDateTimeProvider(Now)
    );

    [Fact]
    public async Task HandleAppendsUserAndGeneratingAssistantAndPublishesTurnRequested()
    {
        LlmModel model = SeedModel();
        ChatThread thread = SeedThreadWithCompletedTurn(model);

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new SendMessageCommand(thread.Id.Value, "Tell me more", model.Id.Value), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(4, thread.Messages.Count);
        Assert.Equal(thread.Id.Value, result.Value.ChatId);

        ChatMessage userMessage = Assert.Single
        (
            thread.Messages,
            message => message.Id.Value == result.Value.UserMessageId && message.Role == MessageRole.User
        );
        ChatMessage assistant = Assert.Single
        (
            thread.Messages,
            message => message.Id.Value == result.Value.AssistantMessageId
                       && message.Role == MessageRole.Assistant
                       && message.Status == MessageStatus.Generating
        );
        Assert.Equal(userMessage.Id, assistant.ParentMessageId);
        Assert.Equal(model.Id, assistant.LlmModelId);

        TurnRequested turnRequested = Assert.IsType<TurnRequested>(Assert.Single(_messageBus.Published));
        Assert.Equal(result.Value.ChatId, turnRequested.ChatId);
        Assert.Equal("auth0|user-1", turnRequested.UserId);
        Assert.Equal(result.Value.AssistantMessageId, turnRequested.AssistantMessageId);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsParentStillGeneratingWhileAssistantIsStillGenerating()
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
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsChatNotFoundWhenChatUnknown()
    {
        LlmModel model = SeedModel();

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new SendMessageCommand(Guid.CreateVersion7(), "Hello", model.Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.NotFound", result.FirstError.Code);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsLlmModelNotFoundWhenModelUnknown()
    {
        LlmModel model = SeedModel();
        ChatThread thread = SeedThreadWithCompletedTurn(model);

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new SendMessageCommand(thread.Id.Value, "Hello", Guid.CreateVersion7()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.LlmModelNotFound", result.FirstError.Code);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsLlmModelDisabledWhenModelDisabled()
    {
        LlmModel model = SeedModel((provider, seededModel) => provider.DisableModel(seededModel.Id));
        ChatThread thread = SeedThreadWithCompletedTurn(model);

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new SendMessageCommand(thread.Id.Value, "Hello", model.Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.LlmModelDisabled", result.FirstError.Code);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsLlmModelDisabledWhenProviderDisabled()
    {
        LlmModel model = SeedModel((provider, _) => provider.Disable());
        ChatThread thread = SeedThreadWithCompletedTurn(model);

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new SendMessageCommand(thread.Id.Value, "Hello", model.Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.LlmModelDisabled", result.FirstError.Code);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }
}