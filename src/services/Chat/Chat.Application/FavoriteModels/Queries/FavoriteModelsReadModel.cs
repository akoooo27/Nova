namespace Chat.Application.FavoriteModels.Queries;

public sealed record FavoriteModelsReadModel
(
    IReadOnlyList<FavoriteLlmModelReadModel> Models
);