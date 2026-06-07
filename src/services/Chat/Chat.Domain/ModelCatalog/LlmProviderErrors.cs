using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

namespace Chat.Domain.ModelCatalog;

public static class LlmProviderErrors
{
    public static Error ModelAlreadyExists(ExternalModelId externalModelId) => Error.Conflict
    (
        code: "LlmProvider.ModelAlreadyExists",
        description: $"Llm provider already contains model with external id '{externalModelId.Value}'."
    );

    public static Error ModelNotFound(LlmModelId modelId) => Error.NotFound
    (
        code: "LlmProvider.ModelNotFound",
        description: $"Llm provider model with id '{modelId.Value}' was not found."
    );

    public static Error CannotDeleteProviderWithModels(LlmProviderId providerId) => Error.Conflict
    (
        code: "LlmProvider.CannotDeleteProviderWithModels",
        description: $"Provider '{providerId.Value}' cannot be deleted while it contains models."
    );
}