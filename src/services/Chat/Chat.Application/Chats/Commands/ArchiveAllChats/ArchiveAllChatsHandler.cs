using Chat.Application.Abstractions.Database;
using Chat.Domain.Chats;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.Chats.Commands.ArchiveAllChats;

internal sealed class ArchiveAllChatsHandler(
    IUserContext userContext,
    IChatRepository chats,
    IUnitOfWork unitOfWork)
    : ICommandHandler<ArchiveAllChatsCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(ArchiveAllChatsCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);

        if (userIdResult.IsError)
        {
            return userIdResult.Errors;
        }

        await chats.ArchiveAllAsync(userIdResult.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}