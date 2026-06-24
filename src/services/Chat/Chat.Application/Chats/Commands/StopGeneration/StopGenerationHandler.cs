using Chat.Application.Abstractions.Turns;
using Chat.Application.Chats.Errors;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.Chats.Commands.StopGeneration;

internal sealed class StopGenerationHandler(IUserContext userContext, IChatRepository chats, ITurnStopSignal stopSignal)
    : ICommandHandler<StopGenerationCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(StopGenerationCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(command.ChatId);
        ErrorOr<ChatMessageId> messageIdResult = ChatMessageId.Create(command.AssistantMessageId);

        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
        }

        if (chatIdResult.IsError)
        {
            errors.AddRange(chatIdResult.Errors);
        }

        if (messageIdResult.IsError)
        {
            errors.AddRange(messageIdResult.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
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
            return ChatOperationErrors.ChatNotFound(chatId);
        }

        ChatMessage? target = thread.FindMessage(messageId);

        if (target is null)
        {
            return ChatErrors.MessageNotFound(messageId);
        }

        if (target.Role != MessageRole.Assistant)
        {
            return ChatErrors.StopTargetMustBeAssistant(messageId);
        }

        if (target.Status != MessageStatus.Generating)
        {
            return ChatErrors.CannotStopNonGenerating(messageId);
        }

        await stopSignal.RequestStopAsync(target.Id.Value, cancellationToken);

        return Result.Success;
    }
}