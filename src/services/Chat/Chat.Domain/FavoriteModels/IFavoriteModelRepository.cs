using Chat.Domain.FavoriteModels.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Domain.FavoriteModels;

public interface IFavoriteModelRepository
{
    Task<FavoriteModel?> GetByIdAsync(FavoriteModelId id, CancellationToken cancellationToken = default);

    Task<FavoriteModel?> GetAsync
    (
        UserId userId,
        LlmModelId llmModelId,
        CancellationToken cancellationToken = default
    );

    void Add(FavoriteModel favoriteModel);

    void Remove(FavoriteModel favoriteModel);
}