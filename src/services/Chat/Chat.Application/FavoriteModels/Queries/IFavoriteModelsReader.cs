using Chat.Domain.Shared;

namespace Chat.Application.FavoriteModels.Queries;

public interface IFavoriteModelsReader
{
    Task<FavoriteModelsReadModel> GetAsync(UserId userId, CancellationToken cancellationToken);
}