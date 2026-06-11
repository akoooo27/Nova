using Chat.Application.Abstractions.ProviderLogos.Errors;
using Chat.Application.Abstractions.ProviderLogos.Results;
using Chat.Application.ModelCatalog.ProviderLogos.Commands.RequestProviderLogoUploadUrl;
using Chat.Application.Tests.ModelCatalog.LlmProviders;
using Chat.Domain.ModelCatalog;

using ErrorOr;

namespace Chat.Application.Tests.ModelCatalog.ProviderLogos.Commands;

public sealed class RequestProviderLogoUploadUrlHandlerTests
{
    [Fact]
    public async Task HandleCreatesUploadUrlForExistingProvider()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider(slug: "anthropic");
        FakeLlmProviderRepository providers = new();
        providers.AddExistingProvider(provider);
        FakeProviderLogoStorage storage = new();
        ProviderLogoUploadUrl uploadUrl = new
        (
            UploadUrl: new Uri("https://assets.example.com/upload"),
            LogoKey: "providers/anthropic/logo.svg",
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(10),
            Headers: new Dictionary<string, string>
            {
                ["Content-Type"] = "image/svg+xml"
            }
        );
        storage.SetUploadUrlResult(uploadUrl);
        RequestProviderLogoUploadUrlHandler handler = new(providers, storage);
        RequestProviderLogoUploadUrlCommand command = new(provider.Id.Value, "image/svg+xml");

        ErrorOr<ProviderLogoUploadUrl> result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Same(uploadUrl, result.Value);
        Assert.Equal(1, storage.CreateUploadUrlCallCount);
        Assert.Equal("anthropic", storage.RequestedProviderSlug);
        Assert.Equal("image/svg+xml", storage.RequestedContentType);
    }

    [Fact]
    public async Task HandlePassesCancellationTokenToStorage()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        FakeLlmProviderRepository providers = new();
        providers.AddExistingProvider(provider);
        FakeProviderLogoStorage storage = new();
        RequestProviderLogoUploadUrlHandler handler = new(providers, storage);
        RequestProviderLogoUploadUrlCommand command = new(provider.Id.Value, "image/png");
        using CancellationTokenSource cancellationTokenSource = new();

        _ = await handler.Handle(command, cancellationTokenSource.Token);

        Assert.Equal(cancellationTokenSource.Token, storage.CreateUploadUrlCancellationToken);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWhenProviderDoesNotExist()
    {
        FakeLlmProviderRepository providers = new();
        FakeProviderLogoStorage storage = new();
        RequestProviderLogoUploadUrlHandler handler = new(providers, storage);
        RequestProviderLogoUploadUrlCommand command = new(Guid.CreateVersion7(), "image/svg+xml");

        ErrorOr<ProviderLogoUploadUrl> result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("LlmProvider.NotFound", error.Code);
        Assert.Equal(0, storage.CreateUploadUrlCallCount);
    }

    [Fact]
    public async Task HandleReturnsValidationErrorWhenProviderIdIsEmpty()
    {
        FakeLlmProviderRepository providers = new();
        FakeProviderLogoStorage storage = new();
        RequestProviderLogoUploadUrlHandler handler = new(providers, storage);
        RequestProviderLogoUploadUrlCommand command = new(Guid.Empty, "image/svg+xml");

        ErrorOr<ProviderLogoUploadUrl> result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal("LlmProviderId.Empty", error.Code);
        Assert.Equal(0, storage.CreateUploadUrlCallCount);
    }

    [Fact]
    public async Task HandleReturnsStorageErrors()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        FakeLlmProviderRepository providers = new();
        providers.AddExistingProvider(provider);
        FakeProviderLogoStorage storage = new();
        storage.SetUploadUrlResult(ProviderLogoOperationErrors.UnsupportedContentType("image/gif"));
        RequestProviderLogoUploadUrlHandler handler = new(providers, storage);
        RequestProviderLogoUploadUrlCommand command = new(provider.Id.Value, "image/gif");

        ErrorOr<ProviderLogoUploadUrl> result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal("ProviderLogo.UnsupportedContentType", error.Code);
        Assert.Equal(1, storage.CreateUploadUrlCallCount);
    }
}