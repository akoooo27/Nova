using Chat.Domain.FavoriteModels;

namespace Chat.Application.FavoriteModels.Results;

internal static class FavoriteModelResultMapper
{
    public static FavoriteModelResult ToResult(this FavoriteModel favoriteModel) => new
    (
        Id: favoriteModel.Id.Value,
        UserId: favoriteModel.UserId.Value,
        LlmModelId: favoriteModel.LlmModelId.Value,
        CreatedAt: favoriteModel.CreatedAt
    );
}