namespace Chat.Api.Endpoints.FavoriteModels.GetFavoriteModels;

internal sealed class Response
{
    public required IReadOnlyCollection<FavoriteLlmModelResponse> Models { get; init; }
}