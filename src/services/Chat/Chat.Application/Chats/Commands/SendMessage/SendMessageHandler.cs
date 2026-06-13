using Chat.Application.Abstractions.Database;
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

namespace Chat.Application.Chats.Commands.SendMessage;

internal sealed class SendMessageHandler(
    IUserContext userContext,
    IChatRepository chats,
    ILlmProviderRepository providers,
    IMessageBus bus,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<SendMessageCommand, ErrorOr<TurnStartedResult>>
{
    public async ValueTask<ErrorOr<TurnStartedResult>> Handle(SendMessageCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(command.ChatId);
        ErrorOr<MessageContent> contentResult = MessageContent.Create(command.Message);
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

        ChatId chatId = chatIdResult.Value;
        UserId userId = userIdResult.Value;
        MessageContent content = contentResult.Value;
        LlmModelId modelId = modelIdResult.Value;

        ErrorOr<Success> usabilityResult = await ModelUsability.EnsureUsableAsync
        (
            providers: providers,
            modelId: modelIdResult.Value,
            cancellationToken: cancellationToken
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

        TurnRequested turnRequested = new
        (
            ChatId: thread.Id.Value,
            UserId: userId.Value,
            AssistantMessageId: assistantMessageId.Value
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
