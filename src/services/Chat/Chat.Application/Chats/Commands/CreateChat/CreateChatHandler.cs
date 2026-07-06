using Chat.Application.Abstractions.Database;
using Chat.Application.Abstractions.Turns;
using Chat.Application.Chats.Results;
using Chat.Application.Turns;
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

namespace Chat.Application.Chats.Commands.CreateChat;

internal sealed class CreateChatHandler(
    IUserContext userContext,
    IChatRepository chats,
    ILlmProviderRepository providers,
    IMessageBus bus,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<CreateChatCommand, ErrorOr<TurnStartedResult>>
{
    public async ValueTask<ErrorOr<TurnStartedResult>> Handle(CreateChatCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<MessageContent> contentResult = MessageContent.Create(command.Message);
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
        LlmModelId modelId = modelIdResult.Value;
        TurnGenerationOptions generationOptions = command.GenerationOptions ?? TurnGenerationOptions.Default;

        ErrorOr<Success> usabilityResult = await ModelUsability.EnsureUsableAsync
        (
            providers: providers,
            modelId: modelId,
            cancellationToken: cancellationToken,
            requiresToolCalling: generationOptions.ForceUseSearch
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
            createdAt: now,
            isTemporary: command.IsTemporary
        );

        if (command.ProjectId is not null)
        {
            ErrorOr<ProjectId> projectIdResult = ProjectId.Create(command.ProjectId.Value);

            if (projectIdResult.IsError)
            {
                return projectIdResult.Errors;
            }

            ProjectId projectId = projectIdResult.Value;

            thread.MoveToProject(projectId, now);
        }

        ChatMessageId userMessageId = thread.CurrentMessageId;

        ErrorOr<ChatMessage> assistantMessageResult = thread.BeginAssistantMessage
        (
            parentMessageId: userMessageId,
            llmModelId: modelId,
            createdAt: now
        );

        if (assistantMessageResult.IsError)
        {
            return assistantMessageResult.Errors;
        }

        ChatMessageId assistantMessageId = assistantMessageResult.Value.Id;

        chats.Add(thread);

        TurnRequested turnRequested = new
        (
            ChatId: thread.Id.Value,
            UserId: userId.Value,
            AssistantMessageId: assistantMessageId.Value,
            Options: generationOptions
        );

        // Published BEFORE SaveChangesAsync on purpose: the MassTransit bus outbox buffers
        // this and writes it to the outbox table inside the same transaction (spec: no dual-write).
        await bus.PublishAsync(turnRequested, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new TurnStartedResult
        (
            ChatId: thread.Id.Value,
            UserMessageId: userMessageId.Value,
            AssistantMessageId: assistantMessageId.Value
        );
    }
}