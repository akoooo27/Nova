namespace Chat.Api.Endpoints.ModelCatalog.ListProviderLogos;

internal sealed class ProviderLogoResponse
{
    public required string Key { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required long Size { get; init; }

    public DateTimeOffset? LastModified { get; init; }
}