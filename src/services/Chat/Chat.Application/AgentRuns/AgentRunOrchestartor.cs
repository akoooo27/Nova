using System.Text;

using Chat.Application.Abstractions.AgentRuns;
using Chat.Application.Abstractions.Database;
using Chat.Application.Abstractions.Turns;
using Chat.Application.Turns;
using Chat.Domain.AgentRuns;
using Chat.Domain.AgentRuns.Entities;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Microsoft.Extensions.Logging;

using SharedKernel;

namespace Chat.Application.AgentRuns;

/// <summary>
/// Pure sequencing of one agent run, kind-agnostic (spec: orchestrators are sequencing only).
/// Everything interesting lives behind a seam; adding behavior here means a new interface,
/// never inline business logic. Mirrors ChatTurnOrchestrator's error contract.
/// </summary>
public sealed partial class AgentRunOrchestrator(
    IChatRepository chats,
    IAgentRunRepository runs,
    IAgentRunnerResolver runnerResolver,
    IAgentRunContextBuilder contextBuilder,
    IWorkflowCheckpointStore checkpointStore,
    ITokenPublisher publisher,
    ITurnStopSignal stopSignal,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    ILogger<AgentRunOrchestrator> logger,
    TimeSpan? maxRunDuration = null)
{
    // Hardcoded default (no options class); the optional parameter is a test seam. DI cannot
    // resolve TimeSpan?, so it falls back to this default at runtime.
    private readonly TimeSpan _maxRunDuration = maxRunDuration ?? TimeSpan.FromMinutes(45);

    public async Task RunAsync(AgentRunRequested job, CancellationToken cancellationToken)
    {
        ErrorOr<ChatId> chatIdResult = ChatId.Create(job.ChatId);
        ErrorOr<UserId> userIdResult = UserId.Create(job.UserId);
        ErrorOr<ChatMessageId> assistantMessageIdResult = ChatMessageId.Create(job.AssistantMessageId);
        ErrorOr<AgentRunId> runIdResult = AgentRunId.Create(job.RunId);

        if (chatIdResult.IsError || userIdResult.IsError || assistantMessageIdResult.IsError || runIdResult.IsError)
        {
            LogMalformedJob(job.ChatId, job.AssistantMessageId);
            return;
        }

        ChatId chatId = chatIdResult.Value;
        UserId userId = userIdResult.Value;
        ChatMessageId assistantMessageId = assistantMessageIdResult.Value;
        AgentRunId runId = runIdResult.Value;

        ChatThread? thread = await chats.GetByIdAsync
        (
            id: chatId,
            userId: userId,
            cancellationToken: cancellationToken
        );

        if (thread is null)
        {
            LogRunTargetMissing(chatId.Value, assistantMessageId.Value);
            return;
        }

        ChatMessage? assistantMessage = thread.FindMessage(assistantMessageId);

        if (assistantMessage is null)
        {
            LogRunTargetMissing(chatId.Value, assistantMessageId.Value);
            return;
        }

        if (assistantMessage.Status != MessageStatus.Generating)
        {
            LogRunAlreadyTerminal(assistantMessageId.Value);
            return;
        }

        AgentRun? run = await runs.GetByIdAsync(runId, cancellationToken);

        if (run is null)
        {
            LogRunRecordMissing(runId.Value, assistantMessageId.Value);

            await FailRunAsync
            (
                thread: thread,
                assistantMessage: assistantMessage,
                run: null,
                reason: "The agent run record is missing.",
                cancellationToken: cancellationToken
            );

            return;
        }

        IAgentRunRunner? runner = runnerResolver.Resolve(run.Kind);

        if (runner is null)
        {
            LogNoRunnerForKind(run.Kind.ToString(), job.AssistantMessageId);

            await FailRunAsync
            (
                thread: thread,
                assistantMessage: assistantMessage,
                run: run,
                reason: $"No runner is registered for agent kind '{run.Kind}'.",
                cancellationToken: cancellationToken
            );

            return;
        }

        WorkflowCheckpoint? checkpoint = await checkpointStore.GetLatestAsync(run.Id.Value, cancellationToken);

        if (checkpoint is null)
        {
            await publisher.ResetAsync(job.AssistantMessageId, cancellationToken);
        }

        ErrorOr<AgentRunContext> contextResult = await contextBuilder.BuildAsync
        (
            thread: thread,
            assistantMessage: assistantMessage,
            run: run,
            cancellationToken: cancellationToken
        );

        if (contextResult.IsError)
        {
            await FailRunAsync
            (
                thread: thread,
                assistantMessage: assistantMessage,
                run: run,
                reason: contextResult.FirstError.Description,
                cancellationToken: cancellationToken
            );

            return;
        }

        AgentRunContext context = contextResult.Value;

        using CancellationTokenSource runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        runCts.CancelAfter(_maxRunDuration);

        StringBuilder reportText = new();

        try
        {
            await foreach (TurnEvent turnEvent in runner.RunAsync(context, checkpoint, runCts.Token))
            {
                if (await stopSignal.IsStopRequestedAsync(job.AssistantMessageId, cancellationToken))
                {
                    await StopRunAsync(thread, assistantMessage, run, cancellationToken);
                    return;
                }

                switch (turnEvent)
                {
                    case TokenEvent token:
                        reportText.Append(token.Text);
                        break;

                    case AgentActivityEvent activity:
                        await AppendActivityAsync
                        (
                            run: run,
                            activity,
                            cancellationToken: cancellationToken
                        );
                        break;

                    case UsageEvent usage:
                        RecordUsage(run, usage);
                        break;
                }

                await publisher.PublishAsync(turnEvent, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (runCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await FailRunAsync
            (
                thread: thread,
                assistantMessage: assistantMessage,
                run: run,
                reason: $"The run exceeded the maximum duration of {_maxRunDuration.TotalMinutes:F0} minutes.",
                CancellationToken.None
            );
            return;
        }
        catch (OperationCanceledException)
        {
            // Worker shutdown mid-run: leave the message Generating; redelivery restarts from
            // scratch (spec decision 2) and stale-sequence skips make replays harmless.
            throw;
        }
#pragma warning disable CA1031 // Last-chance boundary: agent exceptions are unenumerable; an uncaught type leaves the card Generating forever.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogAgentRunFailed(exception, job.AssistantMessageId);
            await FailRunAsync(thread, assistantMessage, run, exception.Message, cancellationToken);
            return;
        }

        string report = reportText.ToString();

        if (report.Length > MessageContent.MaxLength)
        {
            LogReportTruncated(job.AssistantMessageId, report.Length);

            report = report[..MessageContent.MaxLength];
        }

        ErrorOr<MessageContent> contentResult = MessageContent.Create(report);

        if (contentResult.IsError)
        {
            await FailRunAsync
            (
                thread: thread,
                assistantMessage: assistantMessage,
                run: run,
                reason: "The agent returned an empty report.",
                cancellationToken: cancellationToken
            );
            return;
        }

        MessageContent content = contentResult.Value;

        ErrorOr<ChatMessage> completionResult = thread.CompleteAssistantMessage
        (
            messageId: assistantMessage.Id,
            content: content,
            completedAt: dateTimeProvider.UtcNow
        );

        if (completionResult.IsError)
        {
            LogRunAlreadyTerminal(assistantMessageId.Value);
            return;
        }

        FinishRun(run);
        await checkpointStore.DeleteAllAsync(run.Id.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await publisher.PublishAsync(new DoneEvent(job.AssistantMessageId), cancellationToken);
    }

    private async Task AppendActivityAsync
    (
        AgentRun run,
        AgentActivityEvent activity,
        CancellationToken cancellationToken
    )
    {
        if (!Enum.TryParse(activity.Kind, ignoreCase: false, out ActivityKind kind))
        {
            LogInvalidActivitySkipped(run.Id.Value, activity.Sequence, activity.Kind);
            return;
        }

        ErrorOr<ActivitySequence> sequenceResult = ActivitySequence.Create(activity.Sequence);
        ErrorOr<ActivityType> typeResult = ActivityType.Create(activity.Type);
        ErrorOr<ActivityTitle> titleResult = ActivityTitle.Create(activity.Title);
        ErrorOr<ActivityDetail>? detailResult = activity.DetailJson is null
            ? null
            : (ErrorOr<ActivityDetail>?)ActivityDetail.Create(activity.DetailJson);

        if (sequenceResult.IsError || typeResult.IsError || titleResult.IsError || detailResult is { IsError: true })
        {
            LogInvalidActivitySkipped(run.Id.Value, activity.Sequence, activity.Kind);
            return;
        }

        ActivitySequence sequence = sequenceResult.Value;
        ActivityType type = typeResult.Value;
        ActivityTitle title = titleResult.Value;
        ActivityDetail? detail = detailResult?.Value;

        ErrorOr<AgentRunActivity> activityResult = run.AppendActivity
        (
            sequence: sequence,
            kind: kind,
            type: type,
            title: title,
            detail: detail,
            occurredAt: dateTimeProvider.UtcNow
        );

        if (activityResult.IsError)
        {
            LogActivitySkipped(run.Id.Value, activity.Sequence, activityResult.FirstError.Code);
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static void RecordUsage(AgentRun run, UsageEvent usage)
    {
        ErrorOr<TokenUsage> deltaResult = TokenUsage.Create(usage.InputTokens, usage.OutputTokens);

        if (deltaResult.IsError)
        {
            return;
        }

        TokenUsage delta = deltaResult.Value;

        run.RecordUsage(delta);
    }

    private void FinishRun(AgentRun run)
    {
        ErrorOr<Success> finished = run.Finish(dateTimeProvider.UtcNow);

        if (finished.IsError)
        {
            LogRunFinishRejected(run.Id.Value, finished.FirstError.Code);
        }
    }

    private async Task StopRunAsync
    (
        ChatThread thread,
        ChatMessage assistantMessage,
        AgentRun run,
        CancellationToken cancellationToken
    )
    {
        ErrorOr<ChatMessage> stopResult = thread.StopAssistantMessage
        (
            messageId: assistantMessage.Id,
            content: null,
            stoppedAt: dateTimeProvider.UtcNow
        );

        if (stopResult.IsError)
        {
            LogRunAlreadyTerminal(assistantMessage.Id.Value);
            return;
        }

        FinishRun(run);
        await checkpointStore.DeleteAllAsync(run.Id.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await publisher.PublishAsync(new StoppedEvent(assistantMessage.Id.Value), cancellationToken);
    }

    private async Task FailRunAsync
    (
        ChatThread thread,
        ChatMessage assistantMessage,
        AgentRun? run,
        string reason,
        CancellationToken cancellationToken
    )
    {
        string truncated = reason.Length <= FailureReason.MaxLength
            ? reason
            : reason[..FailureReason.MaxLength];

        ErrorOr<FailureReason> failureReason = FailureReason.Create(truncated);

        ErrorOr<ChatMessage> failure = thread.FailAssistantMessage
        (
            messageId: assistantMessage.Id,
            reason: failureReason.IsError
                ? FailureReason.Create("The agent run failed").Value
                : failureReason.Value,
            failedAt: dateTimeProvider.UtcNow
        );

        if (failure.IsError)
        {
            LogRunAlreadyTerminal(assistantMessage.Id.Value);
            return;
        }

        if (run is not null)
        {
            FinishRun(run);
            await checkpointStore.DeleteAllAsync(run.Id.Value, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await publisher.PublishAsync(new FailedEvent(assistantMessage.Id.Value, truncated), cancellationToken);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Discarded malformed agent run job for chat {ChatId}, message {AssistantMessageId}")]
    private partial void LogMalformedJob(Guid chatId, Guid assistantMessageId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Agent run target not found for chat {ChatId}, message {AssistantMessageId}")]
    private partial void LogRunTargetMissing(Guid chatId, Guid assistantMessageId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information,
        Message = "Agent run for message {AssistantMessageId} is already terminal; skipping (idempotent redelivery)")]
    private partial void LogRunAlreadyTerminal(Guid assistantMessageId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error,
        Message = "Agent run failed for message {AssistantMessageId}")]
    private partial void LogAgentRunFailed(Exception exception, Guid assistantMessageId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error,
        Message = "Agent run record {RunId} missing for message {AssistantMessageId}")]
    private partial void LogRunRecordMissing(Guid runId, Guid assistantMessageId);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error,
        Message = "No runner registered for agent kind {Kind}; failing message {AssistantMessageId}")]
    private partial void LogNoRunnerForKind(string kind, Guid assistantMessageId);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning,
        Message = "Skipped unparseable activity (run {RunId}, sequence {Sequence}, kind {Kind})")]
    private partial void LogInvalidActivitySkipped(Guid runId, int sequence, string kind);

    [LoggerMessage(EventId = 8, Level = LogLevel.Information,
        Message = "Skipped activity append (run {RunId}, sequence {Sequence}): {ErrorCode}")]
    private partial void LogActivitySkipped(Guid runId, int sequence, string errorCode);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning,
        Message = "Run {RunId} finish rejected: {ErrorCode}")]
    private partial void LogRunFinishRejected(Guid runId, string errorCode);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning,
        Message = "Report for message {AssistantMessageId} truncated from {Length} characters")]
    private partial void LogReportTruncated(Guid assistantMessageId, int length);
}