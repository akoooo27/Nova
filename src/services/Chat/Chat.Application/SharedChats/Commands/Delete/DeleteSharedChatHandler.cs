using Chat.Application.Abstractions.Database;
using Chat.Application.SharedChats.Errors;
using Chat.Domain.Shared;
using Chat.Domain.SharedChats;
using Chat.Domain.SharedChats.ValueObjects;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.SharedChats.Commands.Delete;

internal sealed class DeleteSharedChatHandler(
    IUserContext userContext,
    ISharedChatRepository sharedChats,
    IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteSharedChatCommand, ErrorOr<Deleted>>
{
    public async ValueTask<ErrorOr<Deleted>> Handle(DeleteSharedChatCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<SharedChatId> sharedChatIdResult = SharedChatId.Create(command.SharedChatId);

        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
        }

        if (sharedChatIdResult.IsError)
        {
            errors.AddRange(sharedChatIdResult.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        UserId userId = userIdResult.Value;
        SharedChatId sharedChatId = sharedChatIdResult.Value;

        SharedChat? sharedChat = await sharedChats.GetByIdAsync
        (
            id: sharedChatId,
            userId: userId,
            cancellationToken: cancellationToken
        );

        if (sharedChat is null)
        {
            return SharedChatOperationErrors.SharedChatNotFound(sharedChatId);
        }

        sharedChats.Remove(sharedChat);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Deleted;
    }
}