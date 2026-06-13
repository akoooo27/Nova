using Chat.Application.Chats.Commands.CreateChat;
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

using ErrorOr;

namespace Chat.Application.Tests.Turns;

public sealed class CreateChatHandlerTests
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

    private CreateChatHandler CreateHandler() => new
    (
        userContext: new FakeUserContext("auth0|user-1"),
        chats: _chats,
        providers: _providers,
        bus: _messageBus,
        unitOfWork: _unitOfWork,
        dateTimeProvider: new FakeDateTimeProvider(Now)
    );

    [Fact]
    public async Task HandlePersistsThreadWithGeneratingAssistantAndPublishesTurnRequested()
    {
        LlmModel model = SeedModel();

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new CreateChatCommand("What is Redis?", model.Id.Value), CancellationToken.None);

        Assert.False(result.IsError);

        ChatThread thread = Assert.Single(_chats.Threads);
        Assert.Equal(result.Value.ChatId, thread.Id.Value);
        Assert.Equal(result.Value.UserMessageId, thread.Messages.Single(x => x.Role == MessageRole.User).Id.Value);

        ChatMessage assistant = Assert.Single
        (
            thread.Messages,
            message => message.Role == MessageRole.Assistant && message.Status == MessageStatus.Generating
        );
        Assert.Equal(result.Value.AssistantMessageId, assistant.Id.Value);
        Assert.Equal(model.Id, assistant.LlmModelId);

        TurnRequested turnRequested = Assert.IsType<TurnRequested>(Assert.Single(_messageBus.Published));
        Assert.Equal(result.Value.ChatId, turnRequested.ChatId);
        Assert.Equal("auth0|user-1", turnRequested.UserId);
        Assert.Equal(result.Value.AssistantMessageId, turnRequested.AssistantMessageId);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsLlmModelNotFoundWhenModelUnknown()
    {
        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new CreateChatCommand("Hello", Guid.CreateVersion7()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.LlmModelNotFound", result.FirstError.Code);
        Assert.Empty(_chats.Threads);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsLlmModelDisabledWhenModelDisabled()
    {
        LlmModel model = SeedModel((provider, seededModel) => provider.DisableModel(seededModel.Id));

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new CreateChatCommand("Hello", model.Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.LlmModelDisabled", result.FirstError.Code);
        Assert.Empty(_chats.Threads);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsLlmModelDisabledWhenProviderDisabled()
    {
        LlmModel model = SeedModel((provider, _) => provider.Disable());

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new CreateChatCommand("Hello", model.Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.LlmModelDisabled", result.FirstError.Code);
        Assert.Empty(_chats.Threads);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }
}