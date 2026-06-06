namespace Chat.Application.FavoriteModels.Queries;

public sealed record FavoriteLlmModelReadModel
(
    Guid Id,
    string ExternalModelId,
    string Name,
    string Description,
    int ContextWindow,
    bool SupportsVision,
    bool SupportsReasoning,
    bool SupportsToolCalling,
    bool IsEnabled,
    FavoriteModelProviderReadModel Provider
);