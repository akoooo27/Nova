using System.Text;

using Chat.Application.Abstractions.Database;
using Chat.Application.Abstractions.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Microsoft.Extensions.Logging;

using SharedKernel;

namespace Chat.Application.Turns;

public sealed partial class ChatTurnOrchestrator(
    IChatRepository chats,
    IMemoryRetriever memoryRetriever,
    ITokenPublisher publisher,
    IContextBuilder contextBuilder,
    IAgentRunner agentRunner,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    ILogger<ChatTurnOrchestrator> logger
)
{
    public async Task RunTurnAsync(TurnRequested job, CancellationToken cancellationToken)
    {
        ErrorOr<ChatId> chatIdResult = ChatId.Create(job.ChatId);
        ErrorOr<UserId> userIdResult = UserId.Create(job.UserId);
        ErrorOr<ChatMessageId> messageIdResult = ChatMessageId.Create(job.AssistantMessageId);

        if (chatIdResult.IsError || userIdResult.IsError || messageIdResult.IsError)
        {
            LogMalformedJob(job.ChatId, job.AssistantMessageId);
            return;
        }

        ChatId chatId = chatIdResult.Value;
        UserId userId = userIdResult.Value;
        ChatMessageId messageId = messageIdResult.Value;

        ChatThread? thread = await chats.GetByIdAsync
        (
            id: chatId,
            userId: userId,
            cancellationToken: cancellationToken
        );

        if (thread is null)
        {
            LogTurnTargetMissing(chatId.Value, messageId.Value);
            return;
        }

        ChatMessage? assistantMessage = thread.FindMessage(messageId);

        if (assistantMessage is null)
        {
            LogTurnTargetMissing(chatId.Value, messageId.Value);
            return;
        }

        if (assistantMessage.Status != MessageStatus.Generating)
        {
            // Redelivery after a finished run — idempotent no-op.
            LogTurnAlreadyTerminal(job.AssistantMessageId);
            return;
        }

        await publisher.ResetAsync(messageId.Value, cancellationToken);

        RetrievedMemories memories = await memoryRetriever.RetrieveAsync(job, cancellationToken);

        ErrorOr<TurnContext> contextResult = await contextBuilder.BuildAsync
        (
            thread: thread,
            assistantMessage: assistantMessage,
            memories: memories,
            generationOptions: job.Options ?? TurnGenerationOptions.Default,
            cancellationToken: cancellationToken
        );

        if (contextResult.IsError)
        {
            await FailTurnAsync
            (
                thread: thread,
                assistantMessage: assistantMessage,
                reason: contextResult.FirstError.Description,
                cancellationToken: cancellationToken
            );
            return;
        }

        StringBuilder text = new();

        try
        {
            await foreach (TurnEvent turnEvent in agentRunner.RunAsync(contextResult.Value, cancellationToken))
            {
                if (turnEvent is TokenEvent token)
                {
                    text.Append(token.Text);
                }

                await publisher.PublishAsync(turnEvent, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown mid-turn: leave the message Generating; redelivery restarts the turn.
            throw;
        }
#pragma warning disable CA1031 // Last-chance boundary: agent/provider exceptions are unenumerable; an uncaught type leaves the message Generating forever and triggers token-duplicating redelivery.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogAgentRunFailed(exception, job.AssistantMessageId);
            await FailTurnAsync
                (
                    thread: thread,
                    assistantMessage: assistantMessage,
                    reason: exception.Message,
                    cancellationToken: cancellationToken
                );
            return;
        }


        ErrorOr<MessageContent> contentResult = MessageContent.Create(text.ToString());

        if (contentResult.IsError)
        {
            await FailTurnAsync
            (
                thread: thread,
                assistantMessage: assistantMessage,
                reason: "The model returned an empty response.",
                cancellationToken: cancellationToken
            );
            return;
        }

        ErrorOr<ChatMessage> completionResult = thread.CompleteAssistantMessage
        (
            messageId: assistantMessage.Id,
            content: contentResult.Value,
            completedAt: dateTimeProvider.UtcNow
        );

        if (completionResult.IsError)
        {
            LogTurnAlreadyTerminal(job.AssistantMessageId);
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await publisher.PublishAsync(new DoneEvent(job.AssistantMessageId), cancellationToken);
    }

    private async Task FailTurnAsync
    (
        ChatThread thread,
        ChatMessage assistantMessage,
        string reason,
        CancellationToken cancellationToken
    )
    {
        string truncated = reason.Length <= FailureReason.MaxLength ? reason : reason[..FailureReason.MaxLength];

        ErrorOr<FailureReason> failureReason = FailureReason.Create(truncated);

        ErrorOr<ChatMessage> failure = thread.FailAssistantMessage
        (
            messageId: assistantMessage.Id,
            reason: failureReason.IsError ? FailureReason.Create("The turn failed.").Value : failureReason.Value,
            failedAt: dateTimeProvider.UtcNow
        );

        if (failure.IsError)
        {
            LogTurnAlreadyTerminal(assistantMessage.Id.Value);
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await publisher.PublishAsync(new FailedEvent(assistantMessage.Id.Value, truncated), cancellationToken);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Discarded malformed turn job for chat {ChatId}, message {AssistantMessageId}")]
    private partial void LogMalformedJob(Guid chatId, Guid assistantMessageId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Turn target not found for chat {ChatId}, message {AssistantMessageId}")]
    private partial void LogTurnTargetMissing(Guid chatId, Guid assistantMessageId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information,
        Message = "Turn {AssistantMessageId} is already terminal; skipping (idempotent redelivery)")]
    private partial void LogTurnAlreadyTerminal(Guid assistantMessageId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error,
        Message = "Agent run failed for turn {AssistantMessageId}")]
    private partial void LogAgentRunFailed(Exception exception, Guid assistantMessageId);
}