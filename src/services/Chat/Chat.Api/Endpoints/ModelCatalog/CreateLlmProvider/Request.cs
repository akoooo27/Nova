namespace Chat.Api.Endpoints.ModelCatalog.CreateLlmProvider;

internal sealed class Request
{
    public required string Name { get; init; }

    public required string Slug { get; init; }

    public int? SortOrder { get; init; }

    public string? LogoKey { get; init; }
}