namespace Chat.Application.ModelCatalog.LlmProviders.Results;

public sealed record LlmProviderResult
(
    Guid Id,
    string Name,
    string Slug,
    int SortOrder,
    string? LogoKey,
    IReadOnlyCollection<LlmModelResult> Models
);