using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

namespace Chat.Application.FavoriteModels.Errors;

public static class FavoriteModelOperationErrors
{
    public static Error LlmModelNotFound(LlmModelId llmModelId) =>
        Error.NotFound
        (
            code: "FavoriteModel.LlmModelNotFound",
            description: $"No LLM model found with ID '{llmModelId.Value}'."
        );

    public static Error LlmModelDisabled(LlmModelId llmModelId) =>
        Error.Conflict
        (
            code: "FavoriteModel.LlmModelDisabled",
            description: $"LLM model '{llmModelId.Value}' is disabled and cannot be favorited."
        );
}