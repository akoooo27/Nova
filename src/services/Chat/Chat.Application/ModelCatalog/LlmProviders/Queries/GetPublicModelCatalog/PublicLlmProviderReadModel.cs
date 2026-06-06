namespace Chat.Application.ModelCatalog.LlmProviders.Queries.GetPublicModelCatalog;

public sealed record PublicLlmProviderReadModel
(
    Guid Id,
    string Name,
    string Slug,
    bool IsFeatured,
    string? LogoKey,
    IReadOnlyCollection<PublicLlmModelReadModel> Models
);