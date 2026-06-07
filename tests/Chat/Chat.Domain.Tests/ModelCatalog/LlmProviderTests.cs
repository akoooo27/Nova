using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.ModelCatalog.Events;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Domain.Tests.ModelCatalog;

public sealed class LlmProviderTests
{
    [Fact]
    public void CreateInitializesProviderWithoutModels()
    {
        ProviderName name = ProviderName.FromDatabase("OpenAI");
        ProviderSlug slug = ProviderSlug.FromDatabase("openai");

        LlmProvider provider = LlmProvider.Create
        (
            name: name,
            slug: slug,
            isFeatured: true
        );

        Assert.NotEqual(Guid.Empty, provider.Id.Value);
        Assert.Equal(name, provider.Name);
        Assert.Equal(slug, provider.Slug);
        Assert.True(provider.IsFeatured);
        Assert.Null(provider.LogoKey);
        Assert.Empty(provider.Models);
    }

    [Fact]
    public void AddModelAddsEnabledModelToProvider()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        ExternalModelId externalModelId = TestCatalogFactory.CreateExternalModelId();
        LlmModelProfile profile = TestCatalogFactory.CreateProfile();
        ErrorOr<LlmModel> result = provider.AddModel
        (
            externalModelId: externalModelId,
            profile: profile
        );

        Assert.False(result.IsError);
        LlmModel model = result.Value;
        Assert.Equal(provider.Id, model.ProviderId);
        Assert.Equal(externalModelId, model.ExternalModelId);
        Assert.Equal(profile, model.Profile);
        Assert.True(model.IsEnabled);
        Assert.Same(model, Assert.Single(provider.Models));
    }

    [Fact]
    public void AddModelReturnsConflictWhenExternalModelIdAlreadyExists()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        ExternalModelId externalModelId = TestCatalogFactory.CreateExternalModelId();
        _ = provider.AddModel
        (
            externalModelId: externalModelId,
            profile: TestCatalogFactory.CreateProfile()
        );

        ErrorOr<LlmModel> result = provider.AddModel
        (
            externalModelId: externalModelId,
            profile: TestCatalogFactory.CreateProfile("GPT-4.1 mini")
        );

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Equal("LlmProvider.ModelAlreadyExists", error.Code);
        Assert.Single(provider.Models);
    }

    [Fact]
    public void UpdateModelProfileUpdatesExistingModelAndReturnsIt()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        LlmModel model = AddModel(provider);
        LlmModelProfile profile = TestCatalogFactory.CreateProfile("GPT-4.1 mini");

        ErrorOr<LlmModel> result = provider.UpdateModelProfile(model.Id, profile);

        Assert.False(result.IsError);
        Assert.Same(model, result.Value);
        Assert.Equal(profile, model.Profile);
    }

    [Fact]
    public void UpdateModelProfileAddsModelProfileUpdatedDomainEvent()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        LlmModel model = AddModel(provider);
        LlmModelProfile profile = TestCatalogFactory.CreateProfile("GPT-4.1 mini");

        ErrorOr<LlmModel> result = provider.UpdateModelProfile(model.Id, profile);

        Assert.False(result.IsError);
        LlmModelProfileUpdated domainEvent = Assert.IsType<LlmModelProfileUpdated>(Assert.Single(provider.DomainEvents));
        Assert.Equal(provider.Id, domainEvent.ProviderId);
        Assert.Equal(model.Id, domainEvent.ModelId);
    }

    [Fact]
    public void UpdateModelProfileReturnsNotFoundWhenModelDoesNotExist()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();

        ErrorOr<LlmModel> result = provider.UpdateModelProfile
        (
            modelId: LlmModelId.New(),
            profile: TestCatalogFactory.CreateProfile()
        );

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("LlmProvider.ModelNotFound", error.Code);
        Assert.Empty(provider.DomainEvents);
    }

    [Fact]
    public void DisableModelDisablesExistingModel()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        LlmModel model = AddModel(provider);

        ErrorOr<Success> result = provider.DisableModel(model.Id);

        Assert.False(result.IsError);
        Assert.False(model.IsEnabled);
    }

    [Fact]
    public void DisableModelReturnsNotFoundWhenModelDoesNotExist()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();

        ErrorOr<Success> result = provider.DisableModel(LlmModelId.New());

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("LlmProvider.ModelNotFound", error.Code);
    }

    [Fact]
    public void EnableModelEnablesExistingModel()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        LlmModel model = AddModel(provider);
        _ = provider.DisableModel(model.Id);

        ErrorOr<Success> result = provider.EnableModel(model.Id);

        Assert.False(result.IsError);
        Assert.True(model.IsEnabled);
    }

    [Fact]
    public void EnableModelReturnsNotFoundWhenModelDoesNotExist()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();

        ErrorOr<Success> result = provider.EnableModel(LlmModelId.New());

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("LlmProvider.ModelNotFound", error.Code);
    }

    [Fact]
    public void UpdateDetailsReplacesProviderDetailsAndAddsDomainEvent()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        ProviderName name = ProviderName.FromDatabase("Anthropic");
        ProviderSlug slug = ProviderSlug.FromDatabase("anthropic");
        AssetKey logoKey = AssetKey.Create("llm-providers/anthropic.svg").Value;

        provider.UpdateDetails(name, slug, logoKey, isFeatured: true);

        Assert.Equal(name, provider.Name);
        Assert.Equal(slug, provider.Slug);
        Assert.Equal(logoKey, provider.LogoKey);
        Assert.True(provider.IsFeatured);
        LlmProviderUpdated domainEvent = Assert.IsType<LlmProviderUpdated>(Assert.Single(provider.DomainEvents));
        Assert.Equal(provider.Id, domainEvent.ProviderId);
    }

    [Fact]
    public void UpdateDetailsDoesNotAddDomainEventWhenDetailsAreUnchanged()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();

        provider.UpdateDetails(provider.Name, provider.Slug, provider.LogoKey, provider.IsFeatured);

        Assert.Empty(provider.DomainEvents);
    }

    [Fact]
    public void UpdateLogoKeyReplacesProviderLogoKey()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        AssetKey logoKey = AssetKey.Create("llm-providers/openai.svg").Value;

        provider.UpdateLogoKey(logoKey);

        Assert.Equal(logoKey, provider.LogoKey);
    }

    [Fact]
    public void RemoveLogoKeyClearsProviderLogoKey()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        AssetKey logoKey = AssetKey.Create("llm-providers/openai.svg").Value;
        provider.UpdateLogoKey(logoKey);

        provider.RemoveLogoKey();

        Assert.Null(provider.LogoKey);
    }

    [Fact]
    public void RemoveFromCatalogReturnsSuccessAndRaisesDeletedEventWhenProviderHasNoModels()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();

        ErrorOr<Success> result = provider.RemoveFromCatalog();

        Assert.False(result.IsError);
        LlmProviderDeleted domainEvent = Assert.IsType<LlmProviderDeleted>(Assert.Single(provider.DomainEvents));
        Assert.Equal(provider.Id, domainEvent.ProviderId);
    }

    [Fact]
    public void RemoveFromCatalogReturnsConflictWithoutRaisingEventWhenProviderHasModels()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        AddModel(provider);

        ErrorOr<Success> result = provider.RemoveFromCatalog();

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Equal("LlmProvider.CannotDeleteProviderWithModels", error.Code);
        Assert.Empty(provider.DomainEvents);
    }

    private static LlmModel AddModel(LlmProvider provider)
    {
        ErrorOr<LlmModel> result = provider.AddModel
        (
            externalModelId: TestCatalogFactory.CreateExternalModelId(),
            profile: TestCatalogFactory.CreateProfile()
        );

        return result.Value;
    }
}