using Chat.Application.Chats.Commands.BranchChat;
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

public sealed class BranchChatHandlerTests
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

    private BranchChatHandler CreateHandler() => new
    (
        userContext: new FakeUserContext("auth0|user-1"),
        chats: _chats,
        providers: _providers,
        bus: _messageBus,
        unitOfWork: _unitOfWork,
        dateTimeProvider: new FakeDateTimeProvider(Now)
    );

    private static ChatThread CreateSourceThread
    (
        LlmModel model,
        string userId = "auth0|user-1",
        bool isTemporary = false
    )
    {
        ChatThread source = ChatThread.Create
        (
            userId: UserId.FromDatabase(userId),
            title: ChatTitle.FromDatabase("Source chat"),
            firstUserMessage: MessageContent.FromDatabase("Original prompt"),
            createdAt: Now.AddHours(-1),
            isTemporary: isTemporary
        );

        ChatMessage assistant = source.BeginAssistantMessage
        (
            parentMessageId: source.CurrentMessageId,
            llmModelId: model.Id,
            createdAt: Now.AddMinutes(-59)
        ).Value;

        _ = source.CompleteAssistantMessage
        (
            messageId: assistant.Id,
            content: MessageContent.FromDatabase("Original answer"),
            completedAt: Now.AddMinutes(-58)
        );

        return source;
    }

    [Fact]
    public async Task HandleCreatesIndependentBranchAndPublishesTurnForNewAssistant()
    {
        LlmModel model = SeedModel();
        ChatThread source = CreateSourceThread(model);
        ChatMessage sourceBranchPoint = source.FindMessage(source.CurrentMessageId)!;
        int sourceMessageCount = source.Messages.Count;
        _chats.Seed(source);

        ErrorOr<TurnStartedResult> result = await CreateHandler().Handle
        (
            new BranchChatCommand
            (
                SourceChatId: source.Id.Value,
                SourceMessageId: sourceBranchPoint.Id.Value,
                Message: "Explore another direction",
                LlmModelId: model.Id.Value
            ),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        ChatThread branch = Assert.Single(_chats.AddedThreads);
        Assert.NotEqual(source.Id, branch.Id);
        Assert.Equal("Branch: Source chat", branch.Title.Value);
        Assert.False(branch.IsTemporary);
        Assert.Equal(source.Id, branch.BranchOrigin!.SourceChatId);
        Assert.Equal(sourceBranchPoint.Id, branch.BranchOrigin.SourceMessageId);
        Assert.Equal(sourceMessageCount, source.Messages.Count);

        ChatMessage newUser = branch.FindMessage(ChatMessageId.FromDatabase(result.Value.UserMessageId))!;
        ChatMessage newAssistant = branch.FindMessage(ChatMessageId.FromDatabase(result.Value.AssistantMessageId))!;
        Assert.Equal(MessageRole.User, newUser.Role);
        Assert.Equal("Explore another direction", newUser.Content!.Value);
        Assert.Equal(MessageRole.Assistant, newAssistant.Role);
        Assert.Equal(MessageStatus.Generating, newAssistant.Status);
        Assert.Equal(newUser.Id, newAssistant.ParentMessageId);
        Assert.Equal(newAssistant.Id, branch.CurrentMessageId);

        TurnRequested turn = Assert.IsType<TurnRequested>(Assert.Single(_messageBus.Published));
        Assert.Equal(branch.Id.Value, turn.ChatId);
        Assert.Equal(newAssistant.Id.Value, turn.AssistantMessageId);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Equal(1, _chats.SnapshotGetCallCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWithoutSideEffectsForForeignSource()
    {
        LlmModel model = SeedModel();
        ChatThread source = CreateSourceThread(model, userId: "auth0|other-user");
        _chats.Seed(source);

        ErrorOr<TurnStartedResult> result = await CreateHandler().Handle
        (
            new BranchChatCommand
            (
                SourceChatId: source.Id.Value,
                SourceMessageId: source.CurrentMessageId.Value,
                Message: "Continue",
                LlmModelId: model.Id.Value
            ),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal("Chat.NotFound", result.FirstError.Code);
        Assert.Empty(_chats.AddedThreads);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWithoutSideEffectsForUnknownSource()
    {
        LlmModel model = SeedModel();

        ErrorOr<TurnStartedResult> result = await CreateHandler().Handle
        (
            new BranchChatCommand
            (
                SourceChatId: Guid.CreateVersion7(),
                SourceMessageId: Guid.CreateVersion7(),
                Message: "Continue",
                LlmModelId: model.Id.Value
            ),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal("Chat.NotFound", result.FirstError.Code);
        Assert.Empty(_chats.AddedThreads);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsBranchConflictWithoutSideEffectsForUserPoint()
    {
        LlmModel model = SeedModel();
        ChatThread source = CreateSourceThread(model);
        ChatMessage root = source.Messages.Single(message => message.ParentMessageId is null);
        _chats.Seed(source);

        ErrorOr<TurnStartedResult> result = await CreateHandler().Handle
        (
            new BranchChatCommand
            (
                SourceChatId: source.Id.Value,
                SourceMessageId: root.Id.Value,
                Message: "Continue",
                LlmModelId: model.Id.Value
            ),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal("Chat.BranchPointMustBeAssistant", result.FirstError.Code);
        Assert.Empty(_chats.AddedThreads);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleRejectsTemporarySourceWithoutSideEffects()
    {
        LlmModel model = SeedModel();
        ChatThread source = CreateSourceThread(model, isTemporary: true);
        _chats.Seed(source);

        ErrorOr<TurnStartedResult> result = await CreateHandler().Handle
        (
            new BranchChatCommand
            (
                SourceChatId: source.Id.Value,
                SourceMessageId: source.CurrentMessageId.Value,
                Message: "Continue",
                LlmModelId: model.Id.Value
            ),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal("Chat.CannotBranchTemporaryChat", result.FirstError.Code);
        Assert.Empty(_chats.AddedThreads);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsLlmModelNotFoundWithoutSideEffects()
    {
        LlmModel model = SeedModel();
        ChatThread source = CreateSourceThread(model);
        _chats.Seed(source);

        ErrorOr<TurnStartedResult> result = await CreateHandler().Handle
        (
            new BranchChatCommand
            (
                SourceChatId: source.Id.Value,
                SourceMessageId: source.CurrentMessageId.Value,
                Message: "Continue",
                LlmModelId: Guid.CreateVersion7()
            ),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal("Chat.LlmModelNotFound", result.FirstError.Code);
        Assert.Empty(_chats.AddedThreads);
        Assert.Empty(_messageBus.Published);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }
}