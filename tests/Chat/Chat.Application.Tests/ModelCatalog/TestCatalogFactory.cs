using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.ValueObjects;

namespace Chat.Application.Tests.ModelCatalog;

internal static class TestCatalogFactory
{
    public static LlmProvider CreateProvider
    (
        string name = "OpenAI",
        string slug = "openai",
        bool isFeatured = false
    )
    {
        return LlmProvider.Create
        (
            name: ProviderName.FromDatabase(name),
            slug: ProviderSlug.FromDatabase(slug),
            isFeatured: isFeatured
        );
    }

    public static LlmModelProfile CreateProfile(string name = "GPT-4.1")
    {
        return LlmModelProfile.Create
        (
            name: ModelName.FromDatabase(name),
            description: ModelDescription.FromDatabase("General purpose model"),
            contextWindow: ContextWindow.FromDatabase(128000),
            capabilities: ModelCapabilities.Create
            (
                supportsVision: true,
                supportsReasoning: false,
                supportsToolCalling: true
            )
        );
    }

    public static ExternalModelId CreateExternalModelId(string value = "gpt-4.1")
    {
        return ExternalModelId.FromDatabase(value);
    }
}