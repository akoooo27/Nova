namespace Chat.Application.ModelCatalog.LlmProviders.Results;

public sealed record LlmProviderResult
(
    Guid Id,
    string Name,
    string Slug,
    bool IsFeatured,
    string? LogoKey,
    IReadOnlyCollection<LlmModelResult> Models
);