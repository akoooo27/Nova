namespace Chat.Application.FavoriteModels.Queries.GetFavoriteModels;

public sealed record FavoriteModelsReadModel
(
    IReadOnlyList<FavoriteLlmModelReadModel> Models
);