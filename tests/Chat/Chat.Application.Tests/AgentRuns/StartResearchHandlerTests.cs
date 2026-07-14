using Chat.Application.AgentRuns;
using Chat.Application.Chats.Commands.StartResearch;
using Chat.Application.Chats.Results;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Tests.ModelCatalog;
using Chat.Application.Tests.ModelCatalog.LlmProviders;
using Chat.Application.Tests.Turns;
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

public sealed class StartResearchHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeChatRepository _chats = new();
    private readonly FakeAgentRunRepository _runs = new();
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
        providers: _providers,
        chats: _chats,
        runs: _runs,
        bus: _messageBus,
        unitOfWork: _unitOfWork,
        dateTimeProvider: new FakeDateTimeProvider(Now)
    );

    [Fact]
    public async Task HandleAppendsUserAndAgentRunAssistantAndRunAndPublishesAgentRunRequested()
    {
        LlmModel model = SeedModel();
        ChatThread thread = SeedThreadWithCompletedTurn(model);

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new StartResearchCommand(thread.Id.Value, "Research quorum systems", model.Id.Value), CancellationToken.None);

        Assert.False(result.IsError);
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
                       && message.Kind == MessageKind.AgentRun
        );
        Assert.Equal(userMessage.Id, assistant.ParentMessageId);
        Assert.Equal(model.Id, assistant.LlmModelId);

        AgentRun run = Assert.Single(_runs.Runs);
        Assert.Equal(AgentRunKind.Research, run.Kind);
        Assert.Equal(thread.Id, run.ChatId);
        Assert.Equal(assistant.Id, run.AssistantMessageId);
        Assert.Equal(model.Id, run.LlmModelId);
        Assert.Equal("Research quorum systems", run.Task.Value);

        AgentRunRequested job = Assert.IsType<AgentRunRequested>(Assert.Single(_messageBus.Published));
        Assert.Equal(result.Value.ChatId, job.ChatId);
        Assert.Equal("auth0|user-1", job.UserId);
        Assert.Equal(result.Value.AssistantMessageId, job.AssistantMessageId);
        Assert.Equal(run.Id.Value, job.RunId);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsChatNotFoundWhenChatUnknown()
    {
        LlmModel model = SeedModel();

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new StartResearchCommand(Guid.CreateVersion7(), "Research something", model.Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.NotFound", result.FirstError.Code);
        Assert.Empty(_runs.Runs);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsCannotStartAgentRunInTemporaryChat()
    {
        LlmModel model = SeedModel();
        ChatThread thread = SeedThreadWithCompletedTurn(model, isTemporary: true);

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new StartResearchCommand(thread.Id.Value, "Research something", model.Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.CannotStartAgentRunInTemporaryChat", result.FirstError.Code);
        Assert.Empty(_runs.Runs);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
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
            .Handle(new StartResearchCommand(thread.Id.Value, "Too eager", model.Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.ParentStillGenerating", result.FirstError.Code);
        Assert.Empty(_runs.Runs);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsLlmModelNotFoundWhenModelUnknown()
    {
        LlmModel model = SeedModel();
        ChatThread thread = SeedThreadWithCompletedTurn(model);

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new StartResearchCommand(thread.Id.Value, "Research something", Guid.CreateVersion7()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.LlmModelNotFound", result.FirstError.Code);
        Assert.Empty(_runs.Runs);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsLlmModelDisabledWhenModelDisabled()
    {
        LlmModel model = SeedModel((provider, seededModel) => provider.DisableModel(seededModel.Id));
        ChatThread thread = SeedThreadWithCompletedTurn(model);

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new StartResearchCommand(thread.Id.Value, "Research something", model.Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.LlmModelDisabled", result.FirstError.Code);
        Assert.Empty(_runs.Runs);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }
}