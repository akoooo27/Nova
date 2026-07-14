using Chat.Application.Abstractions.AgentRuns;
using Chat.Application.AgentRuns;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Tests.Turns;
using Chat.Application.Turns;
using Chat.Domain.AgentRuns;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using Microsoft.Extensions.Logging.Abstractions;

namespace Chat.Application.Tests.AgentRuns;

public sealed class AgentRunOrchestratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeChatRepository _chats = new();
    private readonly FakeAgentRunRepository _runs = new();
    private readonly RecordingTokenPublisher _publisher = new();
    private readonly FakeTurnStopSignal _stopSignal = new();
    private readonly TurnFakeUnitOfWork _unitOfWork = new();

    private (ChatThread Thread, ChatMessage Assistant, AgentRun Run, AgentRunRequested Job) SeedPendingRun()
    {
        ChatThread thread = ChatThread.Create
        (
            userId: UserId.Create("auth0|user-1").Value,
            title: ChatTitle.Create("Research").Value,
            firstUserMessage: MessageContent.Create("Research Redis Streams").Value,
            createdAt: Now
        );

        ChatMessage assistant = thread.BeginAssistantMessage
        (
            parentMessageId: thread.CurrentMessageId,
            llmModelId: LlmModelId.New(),
            createdAt: Now,
            kind: MessageKind.AgentRun
        ).Value;

        AgentRun run = AgentRun.Start
        (
            kind: AgentRunKind.Research,
            chatId: thread.Id,
            assistantMessageId: assistant.Id,
            userId: thread.UserId,
            task: AgentTask.Create("Research Redis Streams").Value,
            llmModelId: LlmModelId.New(),
            startedAt: Now
        );

        _chats.Seed(thread);
        _runs.Seed(run);

        AgentRunRequested job = new(thread.Id.Value, "auth0|user-1", assistant.Id.Value, run.Id.Value);

        return (thread, assistant, run, job);
    }

    private AgentRunOrchestrator CreateOrchestrator
    (
        Func<AgentRunContext, CancellationToken, IAsyncEnumerable<TurnEvent>>? script = null,
        bool withRunner = true,
        TimeSpan? maxRunDuration = null
    )
    {
        FakeAgentRunRunner runner = new(script ?? ((_, ct) => FakeAgentRunRunner.Script([], ct)));

        return new AgentRunOrchestrator
        (
            chats: _chats,
            runs: _runs,
            runnerResolver: new FakeAgentRunnerResolver(withRunner ? runner : null),
            contextBuilder: new FakeAgentRunContextBuilder(),
            checkpointStore: new NoOpWorkflowCheckpointStore(),
            publisher: _publisher,
            stopSignal: _stopSignal,
            unitOfWork: _unitOfWork,
            dateTimeProvider: new FakeDateTimeProvider(Now),
            logger: NullLogger<AgentRunOrchestrator>.Instance,
            maxRunDuration: maxRunDuration ?? TimeSpan.FromMinutes(45)
        );
    }

    private static AgentActivityEvent Activity(Guid turnId, int sequence, string kind = "ToolCall", string type = "web.search") =>
        new(turnId, sequence, kind, type, Title: $"Activity {sequence}", DetailJson: null);

    [Fact]
    public async Task RunAsyncHappyPathAppendsActivitiesRecordsUsageAndCompletesWithReport()
    {
        (_, ChatMessage assistant, AgentRun run, AgentRunRequested job) = SeedPendingRun();
        Guid turnId = assistant.Id.Value;

        TurnEvent[] events =
        [
            Activity(turnId, 1, kind: "Phase", type: "phase"),
            Activity(turnId, 2),
            new UsageEvent(turnId, "gpt-4.1", 120, 45),
            new TokenEvent(turnId, "# Report\n\nFindings.")
        ];

        await CreateOrchestrator(script: (_, ct) => FakeAgentRunRunner.Script(events, ct))
            .RunAsync(job, CancellationToken.None);

        Assert.Equal(MessageStatus.Completed, assistant.Status);
        Assert.Equal("# Report\n\nFindings.", assistant.Content!.Value);
        Assert.Equal(2, run.Activities.Count);
        Assert.Equal(120, run.Usage.InputTokens);
        Assert.Equal(45, run.Usage.OutputTokens);
        Assert.NotNull(run.FinishedAt);
        Assert.Equal(1, _publisher.ResetCount);
        Assert.IsType<DoneEvent>(_publisher.Events[^1]);
        Assert.Equal(3, _unitOfWork.SaveCount); // 2 activity saves + 1 terminal save
    }

    [Fact]
    public async Task RunAsyncStaleSequenceSkipsWithoutFailing()
    {
        (_, ChatMessage assistant, AgentRun run, AgentRunRequested job) = SeedPendingRun();
        Guid turnId = assistant.Id.Value;

        TurnEvent[] events =
        [
            Activity(turnId, 1),
            Activity(turnId, 1),
            Activity(turnId, 2),
            new TokenEvent(turnId, "# Report")
        ];

        await CreateOrchestrator(script: (_, ct) => FakeAgentRunRunner.Script(events, ct))
            .RunAsync(job, CancellationToken.None);

        Assert.Equal(MessageStatus.Completed, assistant.Status);
        Assert.Equal(2, run.Activities.Count);
    }

    [Fact]
    public async Task RunAsyncUnparseableActivityKindSkipsWithoutFailing()
    {
        (_, ChatMessage assistant, AgentRun run, AgentRunRequested job) = SeedPendingRun();
        Guid turnId = assistant.Id.Value;

        TurnEvent[] events =
        [
            Activity(turnId, 1, kind: "Bogus"),
            new TokenEvent(turnId, "# Report")
        ];

        await CreateOrchestrator(script: (_, ct) => FakeAgentRunRunner.Script(events, ct))
            .RunAsync(job, CancellationToken.None);

        Assert.Equal(MessageStatus.Completed, assistant.Status);
        Assert.Empty(run.Activities);
    }

    [Fact]
    public async Task RunAsyncStopRequestedHardStopsWithNullContent()
    {
        (_, ChatMessage assistant, AgentRun run, AgentRunRequested job) = SeedPendingRun();
        Guid turnId = assistant.Id.Value;

        _stopSignal.EnqueueResponse(false);
        _stopSignal.EnqueueResponse(true);

        TurnEvent[] events =
        [
            Activity(turnId, 1),
            Activity(turnId, 2),
            new TokenEvent(turnId, "# Never persisted")
        ];

        await CreateOrchestrator(script: (_, ct) => FakeAgentRunRunner.Script(events, ct))
            .RunAsync(job, CancellationToken.None);

        Assert.Equal(MessageStatus.Stopped, assistant.Status);
        Assert.Null(assistant.Content);
        Assert.NotNull(run.FinishedAt);
        Assert.IsType<StoppedEvent>(_publisher.Events[^1]);
        Assert.DoesNotContain(_publisher.Events, e => e is DoneEvent);
        Assert.Single(run.Activities);
    }

    [Fact]
    public async Task RunAsyncRunnerThrowsFailsMessageAndFinishesRun()
    {
        (_, ChatMessage assistant, AgentRun run, AgentRunRequested job) = SeedPendingRun();
        Guid turnId = assistant.Id.Value;

        await CreateOrchestrator(script: (_, ct) => FakeAgentRunRunner.EventsThenThrow
            (
                [Activity(turnId, 1)],
                new InvalidOperationException("provider down"),
                ct
            ))
            .RunAsync(job, CancellationToken.None);

        Assert.Equal(MessageStatus.Failed, assistant.Status);
        Assert.Contains("provider down", assistant.FailureReason!.Value, StringComparison.Ordinal);
        Assert.NotNull(run.FinishedAt);
        Assert.IsType<FailedEvent>(_publisher.Events[^1]);
        Assert.Single(run.Activities);
    }

    [Fact]
    public async Task RunAsyncMaxDurationExceededFailsTheRun()
    {
        (_, ChatMessage assistant, AgentRun run, AgentRunRequested job) = SeedPendingRun();

        await CreateOrchestrator
            (
                script: (_, ct) => FakeAgentRunRunner.Hang(ct),
                maxRunDuration: TimeSpan.FromMilliseconds(50)
            )
            .RunAsync(job, CancellationToken.None);

        Assert.Equal(MessageStatus.Failed, assistant.Status);
        Assert.Contains("maximum duration", assistant.FailureReason!.Value, StringComparison.Ordinal);
        Assert.NotNull(run.FinishedAt);
        Assert.IsType<FailedEvent>(_publisher.Events[^1]);
    }

    [Fact]
    public async Task RunAsyncWorkerShutdownRethrowsAndLeavesMessageGenerating()
    {
        (_, ChatMessage assistant, _, AgentRunRequested job) = SeedPendingRun();

        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateOrchestrator(script: (_, ct) => FakeAgentRunRunner.Hang(ct))
                .RunAsync(job, cts.Token));

        Assert.Equal(MessageStatus.Generating, assistant.Status);
        Assert.DoesNotContain(_publisher.Events, e => e is FailedEvent);
    }

    [Fact]
    public async Task RunAsyncWhenMessageAlreadyTerminalDoesNothing()
    {
        (ChatThread thread, ChatMessage assistant, _, AgentRunRequested job) = SeedPendingRun();
        thread.CompleteAssistantMessage(assistant.Id, MessageContent.Create("done already").Value, Now);

        await CreateOrchestrator().RunAsync(job, CancellationToken.None);

        Assert.Empty(_publisher.Events);
        Assert.Equal(0, _publisher.ResetCount);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task RunAsyncWhenRunRecordMissingFailsTheMessage()
    {
        (_, ChatMessage assistant, _, AgentRunRequested job) = SeedPendingRun();
        AgentRunRequested bogusJob = job with { RunId = Guid.CreateVersion7() };

        await CreateOrchestrator().RunAsync(bogusJob, CancellationToken.None);

        Assert.Equal(MessageStatus.Failed, assistant.Status);
        Assert.IsType<FailedEvent>(_publisher.Events[^1]);
    }

    [Fact]
    public async Task RunAsyncWhenNoRunnerForKindFailsTheMessage()
    {
        (_, ChatMessage assistant, AgentRun run, AgentRunRequested job) = SeedPendingRun();

        await CreateOrchestrator(withRunner: false).RunAsync(job, CancellationToken.None);

        Assert.Equal(MessageStatus.Failed, assistant.Status);
        Assert.NotNull(run.FinishedAt);
        Assert.IsType<FailedEvent>(_publisher.Events[^1]);
    }

    [Fact]
    public async Task RunAsyncWhenRunnerYieldsNoReportFailsTheRun()
    {
        (_, ChatMessage assistant, AgentRun run, AgentRunRequested job) = SeedPendingRun();
        Guid turnId = assistant.Id.Value;

        await CreateOrchestrator(script: (_, ct) => FakeAgentRunRunner.Script([Activity(turnId, 1)], ct))
            .RunAsync(job, CancellationToken.None);

        Assert.Equal(MessageStatus.Failed, assistant.Status);
        Assert.NotNull(run.FinishedAt);
        Assert.IsType<FailedEvent>(_publisher.Events[^1]);
    }
}