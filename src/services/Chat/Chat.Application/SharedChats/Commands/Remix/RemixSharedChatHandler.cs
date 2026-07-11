using Chat.Application.Abstractions.Database;
using Chat.Application.SharedChats.Errors;
using Chat.Application.SharedChats.Results;
using Chat.Domain.Chats;
using Chat.Domain.Shared;
using Chat.Domain.SharedChats;
using Chat.Domain.SharedChats.ValueObjects;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

using SharedKernel;

namespace Chat.Application.SharedChats.Commands.Remix;

internal sealed class RemixSharedChatHandler(
    IUserContext userContext,
    ISharedChatRepository sharedChats,
    IChatRepository chats,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider)
    : ICommandHandler<RemixSharedChatCommand, ErrorOr<RemixSharedChatResult>>
{
    public async ValueTask<ErrorOr<RemixSharedChatResult>> Handle(RemixSharedChatCommand command, CancellationToken cancellationToken)
    {

        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<SharedChatId> shareIdResult = SharedChatId.Create(command.ShareId);

        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
        }

        if (shareIdResult.IsError)
        {
            errors.AddRange(shareIdResult.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        UserId userId = userIdResult.Value;
        SharedChatId shareId = shareIdResult.Value;

        SharedChat? share = await sharedChats.GetForRemixAsync(shareId, cancellationToken);

        if (share is null)
        {
            return SharedChatOperationErrors.SharedChatNotFound(shareId);
        }

        if (!share.AllowRemix)
        {
            return SharedChatOperationErrors.RemixNotAllowed(shareId);
        }

        ChatThread? source = await chats.GetSnapshotByChatIdAsync(share.ChatId, cancellationToken);

        if (source is null)
        {
            return SharedChatOperationErrors.SharedChatNotFound(shareId);
        }

        DateTimeOffset now = dateTimeProvider.UtcNow;

        ErrorOr<ChatThread> remixResult = ChatThread.CreateRemix
        (
            remixerUserId: userId,
            source: source,
            sharedNodeId: share.CurrentMessageId,
            shareId: share.Id,
            title: share.Title,
            createdAt: now
        );

        if (remixResult.IsError)
        {
            return remixResult.Errors;
        }

        ChatThread remix = remixResult.Value;

        chats.Add(remix);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new RemixSharedChatResult
        (
            ChatId: remix.Id.Value,
            Title: remix.Title.Value,
            CreatedAt: remix.CreatedAt
        );
    }
}