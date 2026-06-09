namespace Chat.Api.Endpoints.ModelCatalog.GetManagedModelCatalog;

internal sealed class ProviderResponse
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Slug { get; init; }

    public required bool IsFeatured { get; init; }

    public string? LogoKey { get; init; }

    public required IReadOnlyCollection<ModelResponse> Models { get; init; }
}