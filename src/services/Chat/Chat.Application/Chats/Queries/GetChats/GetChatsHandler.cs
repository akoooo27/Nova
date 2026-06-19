using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.Chats.Queries.GetChats;

internal sealed class GetChatsHandler(IUserContext userContext, IChatListReader reader)
    : IQueryHandler<GetChatsQuery, ErrorOr<ChatListReadModel>>
{
    public async ValueTask<ErrorOr<ChatListReadModel>> Handle(GetChatsQuery query, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);

        if (userIdResult.IsError)
        {
            return userIdResult.Errors;
        }

        return await reader.GetAsync
        (
            userId: userIdResult.Value,
            limit: query.Limit,
            offset: query.Offset,
            cancellationToken: cancellationToken
        );
    }
}