namespace Chat.Application.ModelCatalog.LlmProviders.Results;

public sealed record LlmProviderResult
(
    Guid Id,
    string Name,
    string Slug,
    bool IsFeatured,
    bool IsEnabled,
    string? LogoKey,
    IReadOnlyCollection<LlmModelResult> Models
);