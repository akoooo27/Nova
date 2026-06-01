using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

namespace Chat.Application.ModelCatalog.LlmProviders.Errors;

public static class LlmProviderOperationFault
{
    public static Error SlugAlreadyExists(ProviderSlug slug) =>
        Error.Conflict
        (
            code: "LlmProvider.SlugAlreadyExists",
            description: $"A provider with the slug '{slug.Value}' already exists."
        );
}