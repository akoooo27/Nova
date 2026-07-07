using Chat.Application.Abstractions.Database;
using Chat.Domain.Chats;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.Chats.Commands.DeleteAllChats;

internal sealed class DeleteAllChatsHandler(
    IUserContext userContext,
    IChatRepository chats,
    IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteAllChatsCommand, ErrorOr<Deleted>>
{
    public async ValueTask<ErrorOr<Deleted>> Handle(DeleteAllChatsCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);

        if (userIdResult.IsError)
        {
            return userIdResult.Errors;
        }

        await chats.DeleteAllAsync(userIdResult.Value, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Deleted;
    }
}
