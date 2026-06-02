using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;
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
        SortOrder sortOrder = SortOrder.FromDatabase(2);

        LlmProvider provider = LlmProvider.Create
        (
            name: name,
            slug: slug,
            sortOrder: sortOrder
        );

        Assert.NotEqual(Guid.Empty, provider.Id.Value);
        Assert.Equal(name, provider.Name);
        Assert.Equal(slug, provider.Slug);
        Assert.Equal(sortOrder, provider.SortOrder);
        Assert.Null(provider.LogoKey);
        Assert.Empty(provider.Models);
    }

    [Fact]
    public void AddModelAddsEnabledModelToProvider()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        ExternalModelId externalModelId = TestCatalogFactory.CreateExternalModelId();
        LlmModelProfile profile = TestCatalogFactory.CreateProfile();
        SortOrder sortOrder = SortOrder.FromDatabase(3);

        ErrorOr<LlmModel> result = provider.AddModel
        (
            externalModelId: externalModelId,
            profile: profile,
            sortOrder: sortOrder
        );

        Assert.False(result.IsError);
        LlmModel model = result.Value;
        Assert.Equal(provider.Id, model.ProviderId);
        Assert.Equal(externalModelId, model.ExternalModelId);
        Assert.Equal(profile, model.Profile);
        Assert.Equal(sortOrder, model.SortOrder);
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
            profile: TestCatalogFactory.CreateProfile(),
            sortOrder: SortOrder.First
        );

        ErrorOr<LlmModel> result = provider.AddModel
        (
            externalModelId: externalModelId,
            profile: TestCatalogFactory.CreateProfile("GPT-4.1 mini"),
            sortOrder: SortOrder.FromDatabase(2)
        );

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Equal("LlmProvider.ModelAlreadyExists", error.Code);
        Assert.Single(provider.Models);
    }

    [Fact]
    public void RefreshModelProfileUpdatesExistingModelAndReturnsIt()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        LlmModel model = AddModel(provider);
        LlmModelProfile profile = TestCatalogFactory.CreateProfile("GPT-4.1 mini");

        ErrorOr<LlmModel> result = provider.RefreshModelProfile(model.Id, profile);

        Assert.False(result.IsError);
        Assert.Same(model, result.Value);
        Assert.Equal(profile, model.Profile);
    }

    [Fact]
    public void RefreshModelProfileReturnsNotFoundWhenModelDoesNotExist()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();

        ErrorOr<LlmModel> result = provider.RefreshModelProfile
        (
            modelId: LlmModelId.New(),
            profile: TestCatalogFactory.CreateProfile()
        );

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("LlmProvider.ModelNotFound", error.Code);
    }

    [Fact]
    public void UpdateModelSortOrderUpdatesExistingModel()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        LlmModel model = AddModel(provider);
        SortOrder sortOrder = SortOrder.FromDatabase(9);

        ErrorOr<Success> result = provider.UpdateModelSortOrder(model.Id, sortOrder);

        Assert.False(result.IsError);
        Assert.Equal(sortOrder, model.SortOrder);
    }

    [Fact]
    public void UpdateModelSortOrderReturnsNotFoundWhenModelDoesNotExist()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();

        ErrorOr<Success> result = provider.UpdateModelSortOrder
        (
            modelId: LlmModelId.New(),
            sortOrder: SortOrder.First
        );

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("LlmProvider.ModelNotFound", error.Code);
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
    public void UpdateSortOrderReplacesProviderSortOrder()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        SortOrder sortOrder = SortOrder.FromDatabase(7);

        provider.UpdateSortOrder(sortOrder);

        Assert.Equal(sortOrder, provider.SortOrder);
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

    private static LlmModel AddModel(LlmProvider provider)
    {
        ErrorOr<LlmModel> result = provider.AddModel
        (
            externalModelId: TestCatalogFactory.CreateExternalModelId(),
            profile: TestCatalogFactory.CreateProfile(),
            sortOrder: SortOrder.First
        );

        return result.Value;
    }
}
