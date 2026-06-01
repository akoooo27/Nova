using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;

namespace Chat.Application.ModelCatalog.LlmProviders.Results;

public static class LlmProviderResultMapper
{
    public static LlmProviderResult ToResult(this LlmProvider provider) => new
        (
            Id: provider.Id.Value,
            Name: provider.Name.Value,
            Slug: provider.Slug.Value,
            SortOrder: provider.SortOrder.Value,
            LogoKey: provider.LogoKey?.Value,
            Models: provider.Models
                .OrderBy(model => model.SortOrder.Value)
                .Select(model => model.ToResult())
                .ToList()
            );

    private static LlmModelResult ToResult(this LlmModel model) => new
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
        SortOrder: model.SortOrder.Value,
        IsEnabled: model.IsEnabled
    );
}