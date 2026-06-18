namespace Chat.Application.ModelCatalog.LlmProviders.Queries.GetManagedModelCatalog;

public sealed record ManagedLlmProviderReadModel
(
    Guid Id,
    string Name,
    string Slug,
    bool IsFeatured,
    bool IsEnabled,
    string? LogoKey,
    IReadOnlyCollection<ManagedLlmModelReadModel> Models
);