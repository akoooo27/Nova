namespace Chat.Application.ModelCatalog.LlmProviders.Queries.GetManagedModelCatalog;

public sealed record ManagedLlmModelReadModel
(
    Guid Id,
    Guid ProviderId,
    string ExternalModelId,
    string Name,
    string Description,
    int ContextWindow,
    bool SupportsVision,
    bool SupportsReasoning,
    bool SupportsToolCalling,
    bool IsEnabled
);