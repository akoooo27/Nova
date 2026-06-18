namespace Chat.Api.Endpoints.ModelCatalog.GetManagedModelCatalog;

internal sealed class Response
{
    public required IReadOnlyCollection<ProviderResponse> Providers { get; init; }
}