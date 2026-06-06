namespace Chat.Api.Endpoints.ModelCatalog.CreateLlmProvider;

internal sealed class Request
{
    public required string Name { get; init; }

    public required string Slug { get; init; }

    public bool IsFeatured { get; init; }

    public string? LogoKey { get; init; }
}