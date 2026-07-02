using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.Chats.Queries.SearchChats;

internal sealed class SearchChatsHandler(IUserContext userContext, IChatSearchReader reader)
    : IQueryHandler<SearchChatsQuery, ErrorOr<ChatSearchReadModel>>
{
    public async ValueTask<ErrorOr<ChatSearchReadModel>> Handle(SearchChatsQuery query, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);

        if (userIdResult.IsError)
        {
            return userIdResult.Errors;
        }

        UserId userId = userIdResult.Value;

        return await reader.SearchAsync
        (
            userId: userId,
            query: query.Query.Trim(),
            isArchived: query.IsArchived,
            limit: query.Limit,
            offset: query.Offset,
            cancellationToken: cancellationToken
        );
    }
}