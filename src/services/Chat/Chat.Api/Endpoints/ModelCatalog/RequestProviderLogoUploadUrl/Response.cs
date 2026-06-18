namespace Chat.Api.Endpoints.ModelCatalog.RequestProviderLogoUploadUrl;

internal sealed class Response
{
    public required Uri UploadUrl { get; init; }

    public required string LogoKey { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public required IReadOnlyDictionary<string, string> Headers { get; init; }
}