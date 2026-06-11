namespace Chat.Api.Endpoints.ModelCatalog.ListProviderLogos;

internal sealed class Response
{
    public required IReadOnlyCollection<ProviderLogoResponse> Logos { get; init; }
}