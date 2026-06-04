namespace Chat.Api.Endpoints.ModelCatalog.GetModelCatalog;

internal sealed class Response
{
    public required IReadOnlyCollection<ProviderResponse> Providers { get; init; }
}