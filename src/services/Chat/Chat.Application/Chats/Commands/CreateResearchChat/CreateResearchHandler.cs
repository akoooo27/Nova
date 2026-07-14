using Chat.Application.Abstractions.Database;
using Chat.Application.AgentRuns;
using Chat.Application.Chats;
using Chat.Application.Chats.Results;
using Chat.Domain.AgentRuns;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;
using Shared.Application.Messaging;

using SharedKernel;

namespace Chat.Application.Chats.Commands.CreateResearchChat;

internal sealed class CreateResearchHandler(
    IUserContext userContext,
    ILlmProviderRepository providers,
    IChatRepository chats,
    IAgentRunRepository runs,
    IMessageBus bus,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<CreateResearchChatCommand, ErrorOr<TurnStartedResult>>
{
    public async ValueTask<ErrorOr<TurnStartedResult>> Handle(CreateResearchChatCommand command,
        CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<MessageContent> contentResult = MessageContent.Create(command.Task);
        ErrorOr<AgentTask> taskResult = AgentTask.Create(command.Task);
        ErrorOr<LlmModelId> modelIdResult = LlmModelId.Create(command.LlmModelId);

        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
        }

        if (contentResult.IsError)
        {
            errors.AddRange(contentResult.Errors);
        }

        if (taskResult.IsError)
        {
            errors.AddRange(taskResult.Errors);
        }

        if (modelIdResult.IsError)
        {
            errors.AddRange(modelIdResult.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        UserId userId = userIdResult.Value;
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

        string titleSource = content.Value.Length <= ChatTitle.MaxLength
            ? content.Value
            : content.Value[..ChatTitle.MaxLength];

        ErrorOr<ChatTitle> titleResult = ChatTitle.Create(titleSource);

        if (titleResult.IsError)
        {
            return titleResult.Errors;
        }

        ChatTitle title = titleResult.Value;

        DateTimeOffset now = dateTimeProvider.UtcNow;

        ChatThread thread = ChatThread.Create
        (
            userId: userId,
            title: title,
            firstUserMessage: content,
            createdAt: now
        );

        if (command.ProjectId is not null)
        {
            ErrorOr<ProjectId> projectIdResult = ProjectId.Create(command.ProjectId.Value);

            if (projectIdResult.IsError)
            {
                return projectIdResult.Errors;
            }

            thread.MoveToProject(projectIdResult.Value, now);
        }

        ChatMessageId userMessageId = thread.CurrentMessageId;

        ErrorOr<ChatMessage> assistantMessageResult = thread.BeginAssistantMessage
        (
            parentMessageId: userMessageId,
            llmModelId: modelId,
            createdAt: now,
            MessageKind.AgentRun
        );

        if (assistantMessageResult.IsError)
        {
            return assistantMessageResult.Errors;
        }

        ChatMessage assistantMessage = assistantMessageResult.Value;

        AgentRun run = AgentRun.Start
        (
            kind: AgentRunKind.Research,
            chatId: thread.Id,
            assistantMessageId: assistantMessage.Id,
            userId: userId,
            task: task,
            llmModelId: modelId,
            startedAt: now
        );

        chats.Add(thread);
        runs.Add(run);

        AgentRunRequested job = new
        (
            ChatId: thread.Id.Value,
            UserId: userId.Value,
            AssistantMessageId: assistantMessage.Id.Value,
            RunId: run.Id.Value
        );

        await bus.PublishAsync(job, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new TurnStartedResult
        (
            ChatId: thread.Id.Value,
            UserMessageId: userMessageId.Value,
            AssistantMessageId: assistantMessage.Id.Value
        );
    }
}