using Chat.Application.Abstractions.ProviderLogos;
using Chat.Application.Abstractions.ProviderLogos.Results;

using ErrorOr;

namespace Chat.Application.Tests.ModelCatalog.ProviderLogos;

internal sealed class FakeProviderLogoStorage : IProviderLogoStorage
{
    private ErrorOr<ProviderLogoUploadUrl> _uploadUrlResult = new ProviderLogoUploadUrl
    (
        UploadUrl: new Uri("https://assets.example.com/upload"),
        LogoKey: "providers/openai/logo.svg",
        ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(10),
        Headers: new Dictionary<string, string>
        {
            ["Content-Type"] = "image/svg+xml"
        }
    );

    private IReadOnlyCollection<ProviderLogoObject> _logos = [];

    public int CreateUploadUrlCallCount { get; private set; }

    public string? RequestedProviderSlug { get; private set; }

    public string? RequestedContentType { get; private set; }

    public CancellationToken CreateUploadUrlCancellationToken { get; private set; }

    public int ListCallCount { get; private set; }

    public CancellationToken ListCancellationToken { get; private set; }

    public void SetUploadUrlResult(ErrorOr<ProviderLogoUploadUrl> result)
    {
        _uploadUrlResult = result;
    }

    public void SetLogos(IReadOnlyCollection<ProviderLogoObject> logos)
    {
        _logos = logos;
    }

    public Task<ErrorOr<ProviderLogoUploadUrl>> CreateUploadUrlAsync
    (
        string providerSlug,
        string contentType,
        CancellationToken cancellationToken
    )
    {
        CreateUploadUrlCallCount++;
        RequestedProviderSlug = providerSlug;
        RequestedContentType = contentType;
        CreateUploadUrlCancellationToken = cancellationToken;

        return Task.FromResult(_uploadUrlResult);
    }

    public Task<IReadOnlyCollection<ProviderLogoObject>> ListAsync(CancellationToken cancellationToken)
    {
        ListCallCount++;
        ListCancellationToken = cancellationToken;

        return Task.FromResult(_logos);
    }
}