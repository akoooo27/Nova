using Chat.Application.Abstractions.ProviderLogos.Results;
using Chat.Application.ModelCatalog.ProviderLogos.Queries.ListProviderLogos;

namespace Chat.Application.Tests.ModelCatalog.ProviderLogos.Queries;

public sealed class ListProviderLogosHandlerTests
{
    [Fact]
    public async Task HandleReturnsProviderLogosFromStorage()
    {
        ProviderLogoObject logo = new
        (
            Key: "providers/openai/logo.svg",
            FileName: "logo.svg",
            ContentType: "image/svg+xml",
            Size: 128,
            LastModified: DateTimeOffset.UtcNow
        );
        FakeProviderLogoStorage storage = new();
        storage.SetLogos([logo]);
        ListProviderLogosHandler handler = new(storage);

        IReadOnlyCollection<ProviderLogoObject> result = await handler.Handle
        (
            new ListProviderLogosQuery(),
            CancellationToken.None
        );

        ProviderLogoObject returnedLogo = Assert.Single(result);
        Assert.Same(logo, returnedLogo);
        Assert.Equal(1, storage.ListCallCount);
    }

    [Fact]
    public async Task HandlePassesCancellationTokenToStorage()
    {
        FakeProviderLogoStorage storage = new();
        ListProviderLogosHandler handler = new(storage);
        using CancellationTokenSource cancellationTokenSource = new();

        _ = await handler.Handle(new ListProviderLogosQuery(), cancellationTokenSource.Token);

        Assert.Equal(cancellationTokenSource.Token, storage.ListCancellationToken);
    }
}