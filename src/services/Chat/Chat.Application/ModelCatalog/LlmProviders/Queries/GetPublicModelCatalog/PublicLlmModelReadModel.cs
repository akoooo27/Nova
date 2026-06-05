namespace Chat.Application.ModelCatalog.LlmProviders.Queries.GetPublicModelCatalog;

public sealed record PublicLlmModelReadModel
(
    Guid Id,
    Guid ProviderId,
    string ExternalModelId,
    string Name,
    string Description,
    int ContextWindow,
    bool SupportsVision,
    bool SupportsReasoning,
    bool SupportsToolCalling
);