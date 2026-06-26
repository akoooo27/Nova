using Chat.Application.Abstractions.Database;
using Chat.Domain.Shared;
using Chat.Domain.SharedChats;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.SharedChats.Commands.DeleteAll;

internal sealed class DeleteAllSharedChatsHandler(
    IUserContext userContext,
    ISharedChatRepository sharedChats,
    IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteAllSharedChatsCommand, ErrorOr<Deleted>>
{
    public async ValueTask<ErrorOr<Deleted>> Handle(DeleteAllSharedChatsCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);

        if (userIdResult.IsError)
        {
            return userIdResult.Errors;
        }

        UserId userId = userIdResult.Value;

        await sharedChats.DeleteAllAsync(userId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Deleted;
    }
}