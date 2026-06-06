using Chat.Domain.ModelCatalog.ValueObjects;

namespace Chat.Domain.Tests.ModelCatalog.ValueObjects;

public sealed class ModelProfileTests
{
    [Fact]
    public void ModelCapabilitiesNoneDisablesEveryCapability()
    {
        ModelCapabilities capabilities = ModelCapabilities.None;

        Assert.False(capabilities.SupportsVision);
        Assert.False(capabilities.SupportsReasoning);
        Assert.False(capabilities.SupportsToolCalling);
    }

    [Fact]
    public void ModelCapabilitiesCreateStoresSelectedCapabilities()
    {
        ModelCapabilities capabilities = ModelCapabilities.Create
        (
            supportsVision: true,
            supportsReasoning: false,
            supportsToolCalling: true
        );

        Assert.True(capabilities.SupportsVision);
        Assert.False(capabilities.SupportsReasoning);
        Assert.True(capabilities.SupportsToolCalling);
    }

    [Fact]
    public void LlmModelProfileCreateStoresProfileParts()
    {
        ModelName name = ModelName.FromDatabase("GPT-4.1");
        ModelDescription description = ModelDescription.FromDatabase("General purpose model");
        ContextWindow contextWindow = ContextWindow.FromDatabase(128000);
        ModelCapabilities capabilities = ModelCapabilities.Create
        (
            supportsVision: true,
            supportsReasoning: true,
            supportsToolCalling: false
        );

        LlmModelProfile profile = LlmModelProfile.Create
        (
            name: name,
            description: description,
            contextWindow: contextWindow,
            capabilities: capabilities
        );

        Assert.Equal(name, profile.Name);
        Assert.Equal(description, profile.Description);
        Assert.Equal(contextWindow, profile.ContextWindow);
        Assert.Equal(capabilities, profile.Capabilities);
    }
}