using Chat.Application.FavoriteModels.Queries;
using Chat.Application.FavoriteModels.Queries.GetFavoriteModels;
using Chat.Domain.Shared;

namespace Chat.Application.Tests.FavoriteModels;

internal sealed class FakeFavoriteModelsReader(FavoriteModelsReadModel readModel) : IFavoriteModelsReader
{
    public UserId? RequestedUserId { get; private set; }

    public int GetCallCount { get; private set; }

    public Task<FavoriteModelsReadModel> GetAsync(UserId userId, CancellationToken cancellationToken)
    {
        RequestedUserId = userId;
        GetCallCount++;

        return Task.FromResult(readModel);
    }
}