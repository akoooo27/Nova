using Chat.Application.ModelCatalog.LlmProviders.Results;

namespace Chat.Api.Endpoints.ModelCatalog.Responses;

internal static class ModelCatalogResponseMapper
{
    public static LlmProviderResponse ToResponse(LlmProviderResult provider) => new()
    {
        Id = provider.Id,
        Name = provider.Name,
        Slug = provider.Slug,
        IsFeatured = provider.IsFeatured,
        LogoKey = provider.LogoKey,
        Models = provider.Models
            .Select(ToResponse)
            .ToList()
    };

    public static LlmModelResponse ToResponse(LlmModelResult model) => new()
    {
        Id = model.Id,
        ProviderId = model.ProviderId,
        ExternalModelId = model.ExternalModelId,
        Name = model.Name,
        Description = model.Description,
        ContextWindow = model.ContextWindow,
        SupportsVision = model.SupportsVision,
        SupportsReasoning = model.SupportsReasoning,
        SupportsToolCalling = model.SupportsToolCalling,
        IsEnabled = model.IsEnabled
    };
}