using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

namespace Chat.Application.ModelCatalog.LlmProviders.Errors;

public static class LlmProviderOperationErrors
{
    public static Error SlugAlreadyExists(ProviderSlug slug) =>
        Error.Conflict
        (
            code: "LlmProvider.SlugAlreadyExists",
            description: $"A provider with the slug '{slug.Value}' already exists."
        );

    public static Error ProviderNotFound(LlmProviderId providerId) =>
        Error.NotFound
        (
            code: "LlmProvider.NotFound",
            description: $"No provider found with ID '{providerId.Value}'."
        );
}