using Chat.Application.FavoriteModels.Queries.GetFavoriteModels;

namespace Chat.Api.Endpoints.FavoriteModels.GetFavoriteModels;

internal static class ResponseMapper
{
    public static Response ToResponse(FavoriteModelsReadModel readModel) => new()
    {
        Models = readModel.Models
            .Select(ToResponse)
            .ToList()
    };

    private static FavoriteLlmModelResponse ToResponse(FavoriteLlmModelReadModel model) => new()
    {
        Id = model.Id,
        ExternalModelId = model.ExternalModelId,
        Name = model.Name,
        Description = model.Description,
        ContextWindow = model.ContextWindow,
        SupportsVision = model.SupportsVision,
        SupportsReasoning = model.SupportsReasoning,
        SupportsToolCalling = model.SupportsToolCalling,
        IsEnabled = model.IsEnabled,
        Provider = new ProviderResponse
        {
            Id = model.Provider.Id,
            Name = model.Provider.Name,
            Slug = model.Provider.Slug,
            LogoKey = model.Provider.LogoKey
        }
    };
}