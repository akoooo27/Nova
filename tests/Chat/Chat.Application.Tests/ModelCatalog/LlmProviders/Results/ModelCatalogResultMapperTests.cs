using Chat.Application.ModelCatalog.LlmProviders.Results;
using Chat.Domain.ModelCatalog;

namespace Chat.Application.Tests.ModelCatalog.LlmProviders.Results;

public sealed class ModelCatalogResultMapperTests
{
    [Fact]
    public void ToResultOrdersModelsByNameAndMapsFeaturedStatus()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider(isFeatured: true);
        _ = provider.AddModel
        (
            externalModelId: TestCatalogFactory.CreateExternalModelId("gpt-z"),
            profile: TestCatalogFactory.CreateProfile("Zulu")
        );
        _ = provider.AddModel
        (
            externalModelId: TestCatalogFactory.CreateExternalModelId("gpt-a"),
            profile: TestCatalogFactory.CreateProfile("Alpha")
        );

        LlmProviderResult result = provider.ToResult();

        Assert.True(result.IsFeatured);
        Assert.Equal(["Alpha", "Zulu"], result.Models.Select(model => model.Name));
    }
}