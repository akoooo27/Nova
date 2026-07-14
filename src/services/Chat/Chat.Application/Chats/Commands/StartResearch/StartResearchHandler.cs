using Chat.Application.Abstractions.Database;
using Chat.Application.AgentRuns;
using Chat.Application.Chats;
using Chat.Application.Chats.Errors;
using Chat.Application.Chats.Results;
using Chat.Domain.AgentRuns;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;
using Shared.Application.Messaging;

using SharedKernel;

namespace Chat.Application.Chats.Commands.StartResearch;

internal sealed class StartResearchHandler(
    IUserContext userContext,
    ILlmProviderRepository providers,
    IChatRepository chats,
    IAgentRunRepository runs,
    IMessageBus bus,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider)
    : ICommandHandler<StartResearchCommand, ErrorOr<TurnStartedResult>>
{
    public async ValueTask<ErrorOr<TurnStartedResult>> Handle(StartResearchCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(command.ChatId);
        ErrorOr<MessageContent> contentResult = MessageContent.Create(command.Task);
        ErrorOr<AgentTask> taskResult = AgentTask.Create(command.Task);
        ErrorOr<LlmModelId> modelIdResult = LlmModelId.Create(command.LlmModelId);

        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
        }

        if (chatIdResult.IsError)
        {
            errors.AddRange(chatIdResult.Errors);
        }

        if (modelIdResult.IsError)
        {
            errors.AddRange(modelIdResult.Errors);
        }

        if (contentResult.IsError)
        {
            errors.AddRange(contentResult.Errors);
        }

        if (taskResult.IsError)
        {
            errors.AddRange(taskResult.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        UserId userId = userIdResult.Value;
        ChatId chatId = chatIdResult.Value;
        MessageContent content = contentResult.Value;
        AgentTask task = taskResult.Value;
        LlmModelId modelId = modelIdResult.Value;

        ErrorOr<Success> usabilityResult = await ModelUsability.EnsureUsableAsync
        (
            providers: providers,
            modelId: modelId,
            cancellationToken: cancellationToken,
            requiresToolCalling: false
        );

        if (usabilityResult.IsError)
        {
            return usabilityResult.Errors;
        }

        ChatThread? thread = await chats.GetByIdAsync
        (
            id: chatId,
            userId: userId,
            cancellationToken: cancellationToken
        );

        if (thread is null)
        {
            return ChatOperationErrors.ChatNotFound(chatId);
        }

        DateTimeOffset now = dateTimeProvider.UtcNow;

        ErrorOr<ChatMessage> userMessageResult = thread.AddUserMessage
        (
            parentMessageId: thread.CurrentMessageId,
            content: content,
            createdAt: now
        );

        if (userMessageResult.IsError)
        {
            return userMessageResult.Errors;
        }

        ChatMessage userMessage = userMessageResult.Value;

        ErrorOr<ChatMessage> assistantMessageResult = thread.BeginAssistantMessage
        (
            parentMessageId: userMessage.Id,
            llmModelId: modelId,
            createdAt: now,
            kind: MessageKind.AgentRun
        );

        if (assistantMessageResult.IsError)
        {
            return assistantMessageResult.Errors;
        }

        ChatMessage assistantMessage = assistantMessageResult.Value;

        AgentRun run = AgentRun.Start
        (
            kind: AgentRunKind.Research,
            chatId: chatId,
            assistantMessageId: assistantMessage.Id,
            userId: userId,
            task: task,
            llmModelId: modelId,
            startedAt: now
        );

        runs.Add(run);

        AgentRunRequested job = new
        (
            ChatId: chatId.Value,
            UserId: userId.Value,
            AssistantMessageId: assistantMessage.Id.Value,
            RunId: run.Id.Value
        );

        await bus.PublishAsync(job, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new TurnStartedResult
        (
            ChatId: thread.Id.Value,
            UserMessageId: userMessageResult.Value.Id.Value,
            AssistantMessageId: assistantMessage.Id.Value
        );
    }
}