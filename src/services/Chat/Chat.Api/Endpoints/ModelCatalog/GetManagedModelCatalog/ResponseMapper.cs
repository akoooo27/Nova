using Chat.Application.ModelCatalog.LlmProviders.Queries.GetManagedModelCatalog;

namespace Chat.Api.Endpoints.ModelCatalog.GetManagedModelCatalog;

internal static class ResponseMapper
{
    public static Response ToResponse(ManagedModelCatalogReadModel catalog) => new()
    {
        Providers = catalog.Providers
            .Select(ToResponse)
            .ToList()
    };

    private static ProviderResponse ToResponse(ManagedLlmProviderReadModel provider) => new()
    {
        Id = provider.Id,
        Name = provider.Name,
        Slug = provider.Slug,
        IsFeatured = provider.IsFeatured,
        IsEnabled = provider.IsEnabled,
        LogoKey = provider.LogoKey,
        Models = provider.Models
            .Select(ToResponse)
            .ToList()
    };

    private static ModelResponse ToResponse(ManagedLlmModelReadModel model) => new()
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