using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;

namespace Chat.Application.ModelCatalog.LlmProviders.Results;

public static class ModelCatalogResultMapper
{
    public static LlmProviderResult ToResult(this LlmProvider provider) => new
        (
            Id: provider.Id.Value,
            Name: provider.Name.Value,
            Slug: provider.Slug.Value,
            IsFeatured: provider.IsFeatured,
            LogoKey: provider.LogoKey?.Value,
            Models: provider.Models
                .OrderBy(model => model.Profile.Name.Value)
                .ThenBy(model => model.Id.Value)
                .Select(model => model.ToResult())
                .ToList()
            );

    public static LlmModelResult ToResult(this LlmModel model) => new
    (
        Id: model.Id.Value,
        ProviderId: model.ProviderId.Value,
        ExternalModelId: model.ExternalModelId.Value,
        Name: model.Profile.Name.Value,
        Description: model.Profile.Description.Value,
        ContextWindow: model.Profile.ContextWindow.Value,
        SupportsVision: model.Profile.Capabilities.SupportsVision,
        SupportsReasoning: model.Profile.Capabilities.SupportsReasoning,
        SupportsToolCalling: model.Profile.Capabilities.SupportsToolCalling,
        IsEnabled: model.IsEnabled
    );
}