using Chat.Application.AgentRuns;
using Chat.Application.Chats.Commands.CreateResearchChat;
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
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

namespace Chat.Application.Tests.AgentRuns;

public sealed class CreateResearchHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeChatRepository _chats = new();
    private readonly FakeAgentRunRepository _runs = new();
    private readonly FakeLlmProviderRepository _providers = new();
    private readonly FakeMessageBus _messageBus = new();
    private readonly TurnFakeUnitOfWork _unitOfWork = new();

    private LlmModel SeedModel(Action<LlmProvider, LlmModel>? configure = null, LlmModelProfile? profile = null)
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        LlmModel model = provider.AddModel
        (
            externalModelId: TestCatalogFactory.CreateExternalModelId(),
            profile: profile ?? TestCatalogFactory.CreateProfile()
        ).Value;

        configure?.Invoke(provider, model);
        _providers.AddExistingProvider(provider);

        return model;
    }

    private CreateResearchHandler CreateHandler() => new
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
    public async Task HandlePersistsThreadWithAgentRunAssistantAndRunAndPublishesAgentRunRequested()
    {
        LlmModel model = SeedModel();

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new CreateResearchChatCommand("Research the history of Redis", model.Id.Value), CancellationToken.None);

        Assert.False(result.IsError);

        ChatThread thread = Assert.Single(_chats.Threads);
        Assert.Equal(result.Value.ChatId, thread.Id.Value);
        Assert.False(thread.IsTemporary);

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
        Assert.Equal("Research the history of Redis", run.Task.Value);

        AgentRunRequested job = Assert.IsType<AgentRunRequested>(Assert.Single(_messageBus.Published));
        Assert.Equal(result.Value.ChatId, job.ChatId);
        Assert.Equal("auth0|user-1", job.UserId);
        Assert.Equal(result.Value.AssistantMessageId, job.AssistantMessageId);
        Assert.Equal(run.Id.Value, job.RunId);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleMovesThreadToProjectWhenProjectIdProvided()
    {
        LlmModel model = SeedModel();
        Guid projectId = Guid.CreateVersion7();

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new CreateResearchChatCommand("Investigate CAP theorem", model.Id.Value, projectId), CancellationToken.None);

        Assert.False(result.IsError);

        ChatThread thread = Assert.Single(_chats.Threads);
        Assert.Equal(projectId, thread.ProjectId?.Value);
    }

    [Fact]
    public async Task HandleAcceptsModelWithoutToolCallingSupport()
    {
        LlmModelProfile noToolCalling = LlmModelProfile.Create
        (
            name: ModelName.FromDatabase("Reasoner"),
            description: ModelDescription.FromDatabase("No tools"),
            contextWindow: ContextWindow.FromDatabase(128000),
            capabilities: ModelCapabilities.Create
            (
                supportsVision: false,
                supportsReasoning: true,
                supportsToolCalling: false
            )
        );
        LlmModel model = SeedModel(profile: noToolCalling);

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new CreateResearchChatCommand("Research something", model.Id.Value), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(_runs.Runs);
    }

    [Fact]
    public async Task HandleReturnsLlmModelNotFoundWhenModelUnknown()
    {
        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new CreateResearchChatCommand("Research something", Guid.CreateVersion7()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.LlmModelNotFound", result.FirstError.Code);
        Assert.Empty(_chats.Threads);
        Assert.Empty(_runs.Runs);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsLlmModelDisabledWhenModelDisabled()
    {
        LlmModel model = SeedModel((provider, seededModel) => provider.DisableModel(seededModel.Id));

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new CreateResearchChatCommand("Research something", model.Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.LlmModelDisabled", result.FirstError.Code);
        Assert.Empty(_chats.Threads);
        Assert.Empty(_runs.Runs);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }
}