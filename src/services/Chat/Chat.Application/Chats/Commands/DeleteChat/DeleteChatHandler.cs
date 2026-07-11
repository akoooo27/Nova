using Chat.Application.Abstractions.Database;
using Chat.Application.Chats.Errors;
using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.Chats.Commands.DeleteChat;

internal sealed class DeleteChatHandler(
    IUserContext userContext,
    IChatRepository chats,
    IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteChatCommand, ErrorOr<Deleted>>
{
    public async ValueTask<ErrorOr<Deleted>> Handle(DeleteChatCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(command.ChatId);

        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
        }

        if (chatIdResult.IsError)
        {
            errors.AddRange(chatIdResult.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        ChatId chatId = chatIdResult.Value;

        int deleted = await chats.DeleteByIdAsync
        (
            id: chatId,
            userId: userIdResult.Value,
            cancellationToken: cancellationToken
        );

        if (deleted == 0)
        {
            return ChatOperationErrors.ChatNotFound(chatId);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Deleted;
    }
}