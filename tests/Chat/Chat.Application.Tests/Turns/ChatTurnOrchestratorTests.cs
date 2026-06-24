using Chat.Application.Abstractions.Turns;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using Microsoft.Extensions.Logging.Abstractions;

namespace Chat.Application.Tests.Turns;

public sealed class ChatTurnOrchestratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeChatRepository _chats = new();
    private readonly RecordingTokenPublisher _publisher = new();
    private readonly FakeTurnStopSignal _stopSignal = new();
    private readonly TurnFakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task RunTurnAsyncHappyPathCompletesMessageAndPublishesDoneLast()
    {
        (_, ChatMessage assistant, TurnRequested job) = SeedPendingTurn();

        FakeAgentRunner runner = new(ctx => FakeAgentRunner.Tokens(ctx.TurnId, ["Hello", " world"]));

        await CreateOrchestrator(runner).RunTurnAsync(job, CancellationToken.None);

        Assert.Equal(MessageStatus.Completed, assistant.Status);
        Assert.Equal("Hello world", assistant.Content!.Value);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Equal(1, _publisher.ResetCount);
        Assert.IsType<DoneEvent>(_publisher.Events[^1]);
        Assert.Equal(2, _publisher.Events.OfType<TokenEvent>().Count());
    }

    [Fact]
    public async Task RunTurnAsyncWhenAgentThrowsFailsMessageAndPublishesFailedEvent()
    {
        (_, ChatMessage assistant, TurnRequested job) = SeedPendingTurn();

        FakeAgentRunner runner = new(ctx =>
            FakeAgentRunner.TokenThenThrow(ctx.TurnId, "partial", new InvalidOperationException("provider down")));

        await CreateOrchestrator(runner).RunTurnAsync(job, CancellationToken.None);

        Assert.Equal(MessageStatus.Failed, assistant.Status);
        Assert.Contains("provider down", assistant.FailureReason!.Value, StringComparison.Ordinal);
        Assert.Equal(1, _unitOfWork.SaveCount);

        FailedEvent failed = Assert.IsType<FailedEvent>(_publisher.Events[^1]);
        Assert.Equal(assistant.Id.Value, failed.TurnId);
        Assert.Contains("provider down", failed.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTurnAsyncWhenMessageAlreadyTerminalDoesNothing()
    {
        (ChatThread thread, ChatMessage assistant, TurnRequested job) = SeedPendingTurn();
        thread.CompleteAssistantMessage
        (
            messageId: assistant.Id,
            content: MessageContent.Create("done already").Value,
            completedAt: Now
        );

        FakeAgentRunner runner = new(ctx => FakeAgentRunner.Tokens(ctx.TurnId, ["should not run"]));

        await CreateOrchestrator(runner).RunTurnAsync(job, CancellationToken.None);

        Assert.Empty(_publisher.Events);
        Assert.Equal(0, _publisher.ResetCount);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task RunTurnAsyncWhenAgentReturnsNoTextFailsTheTurn()
    {
        (_, ChatMessage assistant, TurnRequested job) = SeedPendingTurn();

        FakeAgentRunner runner = new(ctx => FakeAgentRunner.Tokens(ctx.TurnId, []));

        await CreateOrchestrator(runner).RunTurnAsync(job, CancellationToken.None);

        Assert.Equal(MessageStatus.Failed, assistant.Status);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.IsType<FailedEvent>(_publisher.Events[^1]);
    }

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

    private (ChatThread Thread, ChatMessage Assistant, TurnRequested Job) SeedPendingTurn()
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

        TurnRequested job = new
        (
            ChatId: thread.Id.Value,
            UserId: "auth0|user-1",
            AssistantMessageId: assistant.Id.Value
        );

        return (thread, assistant, job);
    }

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
}