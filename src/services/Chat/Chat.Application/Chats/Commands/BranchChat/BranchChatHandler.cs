using Chat.Application.Abstractions.Database;
using Chat.Application.Abstractions.Turns;
using Chat.Application.Chats.Errors;
using Chat.Application.Chats.Results;
using Chat.Application.Turns;
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

namespace Chat.Application.Chats.Commands.BranchChat;

internal sealed class BranchChatHandler(
    IUserContext userContext,
    IChatRepository chats,
    ILlmProviderRepository providers,
    IMessageBus bus,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<BranchChatCommand, ErrorOr<TurnStartedResult>>
{
    public async ValueTask<ErrorOr<TurnStartedResult>> Handle(BranchChatCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> sourceChatIdResult = ChatId.Create(command.SourceChatId);
        ErrorOr<ChatMessageId> sourceMessageIdResult = ChatMessageId.Create(command.SourceMessageId);
        ErrorOr<MessageContent> contentResult = MessageContent.Create(command.Message);
        ErrorOr<LlmModelId> modelIdResult = LlmModelId.Create(command.LlmModelId);

        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
        }

        if (sourceChatIdResult.IsError)
        {
            errors.AddRange(sourceChatIdResult.Errors);
        }

        if (sourceMessageIdResult.IsError)
        {
            errors.AddRange(sourceMessageIdResult.Errors);
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
        ChatId sourceChatId = sourceChatIdResult.Value;
        ChatMessageId sourceMessageId = sourceMessageIdResult.Value;
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

        DateTimeOffset now = dateTimeProvider.UtcNow;

        ChatThread? source = await chats.GetSnapshotByIdAsync
        (
            id: sourceChatId,
            userId: userId,
            cancellationToken: cancellationToken
        );

        if (source is null)
        {
            return ChatOperationErrors.ChatNotFound(sourceChatId);
        }

        ErrorOr<ChatThread> branchResult = ChatThread.BranchFrom
        (
            source: source,
            branchPointId: sourceMessageId,
            createdAt: now
        );

        if (branchResult.IsError)
        {
            return branchResult.Errors;
        }

        ChatThread thread = branchResult.Value;

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

        ChatMessageId userMessageId = userMessageResult.Value.Id;

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