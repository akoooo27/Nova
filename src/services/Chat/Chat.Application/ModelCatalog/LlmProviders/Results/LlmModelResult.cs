namespace Chat.Application.ModelCatalog.LlmProviders.Results;

public sealed record LlmModelResult
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