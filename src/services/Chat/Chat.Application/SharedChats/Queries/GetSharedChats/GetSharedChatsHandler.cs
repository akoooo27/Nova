using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.SharedChats.Queries.GetSharedChats;

internal sealed class GetSharedChatsHandler(IUserContext userContext, ISharedChatListReader reader)
    : IQueryHandler<GetSharedChatsQuery, ErrorOr<SharedChatListReadModel>>
{
    public async ValueTask<ErrorOr<SharedChatListReadModel>> Handle(GetSharedChatsQuery query, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);

        if (userIdResult.IsError)
        {
            return userIdResult.Errors;
        }

        UserId userId = userIdResult.Value;

        return await reader.GetAsync
        (
            userId: userId,
            limit: query.Limit,
            offset: query.Offset,
            cancellationToken: cancellationToken
        );
    }
}