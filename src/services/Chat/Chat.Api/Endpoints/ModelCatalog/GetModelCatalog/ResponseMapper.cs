using Chat.Application.ModelCatalog.LlmProviders.Queries.GetPublicModelCatalog;

namespace Chat.Api.Endpoints.ModelCatalog.GetModelCatalog;

internal static class ResponseMapper
{
    public static Response ToResponse(PublicModelCatalogReadModel catalog) => new()
    {
        Providers = catalog.Providers
            .Select(ToResponse)
            .ToList()
    };

    private static ProviderResponse ToResponse(PublicLlmProviderReadModel provider) => new()
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

    private static ModelResponse ToResponse(PublicLlmModelReadModel model) => new()
    {
        Id = model.Id,
        ProviderId = model.ProviderId,
        ExternalModelId = model.ExternalModelId,
        Name = model.Name,
        Description = model.Description,
        ContextWindow = model.ContextWindow,
        SupportsVision = model.SupportsVision,
        SupportsReasoning = model.SupportsReasoning,
        SupportsToolCalling = model.SupportsToolCalling
    };
}