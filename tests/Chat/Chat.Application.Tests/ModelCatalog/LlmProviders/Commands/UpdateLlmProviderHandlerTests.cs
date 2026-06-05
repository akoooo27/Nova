using Chat.Application.ModelCatalog.LlmProviders.Commands.UpdateLlmProvider;
using Chat.Application.ModelCatalog.LlmProviders.Results;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Events;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.ModelCatalog.LlmProviders.Commands;

public sealed class UpdateLlmProviderHandlerTests
{
    [Fact]
    public async Task HandleUpdatesProviderDetailsRemovesLogoAndSavesChanges()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        provider.UpdateLogoKey(AssetKey.Create("llm-providers/openai.svg").Value);
        FakeLlmProviderRepository providers = new();
        providers.AddExistingProvider(provider);
        FakeUnitOfWork unitOfWork = new();
        UpdateLlmProviderHandler handler = new(providers, unitOfWork);
        UpdateLlmProviderCommand command = new
        (
            ProviderId: provider.Id.Value,
            Name: "OpenAI Platform",
            Slug: provider.Slug.Value,
            LogoKey: null,
            IsFeatured: true
        );

        ErrorOr<LlmProviderResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsError);
        LlmProviderResult updatedProvider = result.Value;
        Assert.Equal(command.Name, updatedProvider.Name);
        Assert.Equal(command.Slug, updatedProvider.Slug);
        Assert.Null(updatedProvider.LogoKey);
        Assert.True(updatedProvider.IsFeatured);
        Assert.Equal(command.Name, provider.Name.Value);
        Assert.Null(provider.LogoKey);
        LlmProviderUpdated domainEvent = Assert.IsType<LlmProviderUpdated>(Assert.Single(provider.DomainEvents));
        Assert.Equal(provider.Id, domainEvent.ProviderId);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsConflictWhenSlugBelongsToAnotherProvider()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        LlmProvider otherProvider = TestCatalogFactory.CreateProvider("Anthropic", "anthropic");
        FakeLlmProviderRepository providers = new();
        providers.AddExistingProvider(provider);
        providers.AddExistingProvider(otherProvider);
        FakeUnitOfWork unitOfWork = new();
        UpdateLlmProviderHandler handler = new(providers, unitOfWork);
        UpdateLlmProviderCommand command = new
        (
            ProviderId: provider.Id.Value,
            Name: "OpenAI",
            Slug: otherProvider.Slug.Value,
            LogoKey: null,
            IsFeatured: false
        );

        ErrorOr<LlmProviderResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Equal("LlmProvider.SlugAlreadyExists", error.Code);
        Assert.Equal("openai", provider.Slug.Value);
        Assert.Empty(provider.DomainEvents);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWhenProviderDoesNotExist()
    {
        FakeLlmProviderRepository providers = new();
        FakeUnitOfWork unitOfWork = new();
        UpdateLlmProviderHandler handler = new(providers, unitOfWork);
        UpdateLlmProviderCommand command = new
        (
            ProviderId: Guid.CreateVersion7(),
            Name: "OpenAI",
            Slug: "openai",
            LogoKey: null,
            IsFeatured: true
        );

        ErrorOr<LlmProviderResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("LlmProvider.NotFound", error.Code);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsValidationErrorsWithoutSaving()
    {
        FakeLlmProviderRepository providers = new();
        FakeUnitOfWork unitOfWork = new();
        UpdateLlmProviderHandler handler = new(providers, unitOfWork);
        UpdateLlmProviderCommand command = new
        (
            ProviderId: Guid.Empty,
            Name: "",
            Slug: "",
            LogoKey: "",
            IsFeatured: false
        );

        ErrorOr<LlmProviderResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, error => error.Code == "LlmProviderId.Empty");
        Assert.Contains(result.Errors, error => error.Code == "ProviderName.Required");
        Assert.Contains(result.Errors, error => error.Code == "ProviderSlug.Required");
        Assert.Contains(result.Errors, error => error.Code == "AssetKey.Required");
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}