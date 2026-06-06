using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.FavoriteModels.Queries;

internal sealed class GetFavoriteModelsHandler(IUserContext userContext, IFavoriteModelsReader reader)
    : IQueryHandler<GetFavoriteModelsQuery, ErrorOr<FavoriteModelsReadModel>>
{
    public async ValueTask<ErrorOr<FavoriteModelsReadModel>> Handle
    (
        GetFavoriteModelsQuery query,
        CancellationToken cancellationToken
    )
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);

        if (userIdResult.IsError)
        {
            return userIdResult.Errors;
        }

        return await reader.GetAsync(userIdResult.Value, cancellationToken);
    }
}