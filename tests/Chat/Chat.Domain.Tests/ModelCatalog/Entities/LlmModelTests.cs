using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Tests.ModelCatalog;

namespace Chat.Domain.Tests.ModelCatalog.Entities;

public sealed class LlmModelTests
{
    [Fact]
    public void CreateInitializesEnabledModelWithProviderExternalIdAndProfile()
    {
        LlmProviderId providerId = LlmProviderId.New();
        ExternalModelId externalModelId = TestCatalogFactory.CreateExternalModelId();
        LlmModelProfile profile = TestCatalogFactory.CreateProfile();
        LlmModel model = LlmModel.Create
        (
            providerId: providerId,
            externalModelId: externalModelId,
            profile: profile
        );

        Assert.NotEqual(Guid.Empty, model.Id.Value);
        Assert.Equal(providerId, model.ProviderId);
        Assert.Equal(externalModelId, model.ExternalModelId);
        Assert.Equal(profile, model.Profile);
        Assert.True(model.IsEnabled);
    }

    [Fact]
    public void DisableMarksModelDisabled()
    {
        LlmModel model = CreateModel();

        model.Disable();

        Assert.False(model.IsEnabled);
    }

    [Fact]
    public void EnableMarksModelEnabled()
    {
        LlmModel model = CreateModel();
        model.Disable();

        model.Enable();

        Assert.True(model.IsEnabled);
    }

    [Fact]
    public void UpdateProfileReplacesProfile()
    {
        LlmModel model = CreateModel();
        LlmModelProfile profile = TestCatalogFactory.CreateProfile("GPT-4.1 mini");

        model.UpdateProfile(profile);

        Assert.Equal(profile, model.Profile);
    }

    private static LlmModel CreateModel()
    {
        return LlmModel.Create
        (
            providerId: LlmProviderId.New(),
            externalModelId: TestCatalogFactory.CreateExternalModelId(),
            profile: TestCatalogFactory.CreateProfile()
        );
    }
}