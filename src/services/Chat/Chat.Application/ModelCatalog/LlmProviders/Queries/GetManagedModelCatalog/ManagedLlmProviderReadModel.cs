namespace Chat.Application.ModelCatalog.LlmProviders.Queries.GetManagedModelCatalog;

public sealed record ManagedLlmProviderReadModel
(
    Guid Id,
    string Name,
    string Slug,
    bool IsFeatured,
    string? LogoKey,
    IReadOnlyCollection<ManagedLlmModelReadModel> Models
);