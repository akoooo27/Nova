using Chat.Domain.FavoriteModels;
using Chat.Domain.FavoriteModels.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Application.Tests.FavoriteModels;

internal sealed class FakeFavoriteModelRepository : IFavoriteModelRepository
{
    private readonly List<FavoriteModel> _favoriteModels = [];
    private readonly List<FavoriteModel> _removedFavoriteModels = [];

    public IReadOnlyCollection<FavoriteModel> FavoriteModels => _favoriteModels;

    public IReadOnlyCollection<FavoriteModel> RemovedFavoriteModels => _removedFavoriteModels;

    public void AddExistingFavoriteModel(FavoriteModel favoriteModel)
    {
        _favoriteModels.Add(favoriteModel);
    }

    public Task<FavoriteModel?> GetByIdAsync(FavoriteModelId id, CancellationToken cancellationToken = default)
    {
        FavoriteModel? favoriteModel = _favoriteModels.FirstOrDefault(x => x.Id == id);

        return Task.FromResult(favoriteModel);
    }

    public Task<FavoriteModel?> GetAsync
    (
        UserId userId,
        LlmModelId llmModelId,
        CancellationToken cancellationToken = default
    )
    {
        FavoriteModel? favoriteModel = _favoriteModels.FirstOrDefault
        (
            x => x.UserId == userId && x.LlmModelId == llmModelId
        );

        return Task.FromResult(favoriteModel);
    }

    public void Add(FavoriteModel favoriteModel)
    {
        _favoriteModels.Add(favoriteModel);
    }

    public void Remove(FavoriteModel favoriteModel)
    {
        _favoriteModels.Remove(favoriteModel);
        _removedFavoriteModels.Add(favoriteModel);
    }
}