namespace Chat.Api.Endpoints.ModelCatalog.UpdateLlmProvider;

internal sealed class Request
{
    public required string Name { get; init; }

    public required string Slug { get; init; }

    public string? LogoKey { get; init; }

    public required bool IsFeatured { get; init; }
}