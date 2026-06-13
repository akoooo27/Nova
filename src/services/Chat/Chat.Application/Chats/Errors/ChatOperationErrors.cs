using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

namespace Chat.Application.Chats.Errors;

public static class ChatOperationErrors
{
    public static Error ChatNotFound(ChatId chatId) =>
        Error.NotFound
        (
            code: "Chat.NotFound",
            description: $"No chat found with id '{chatId.Value}'."
        );

    public static Error LlmModelNotFound(LlmModelId modelId) =>
        Error.NotFound
        (
            code: "Chat.LlmModelNotFound",
            description: $"No enabled LLM model found with id '{modelId.Value}'."
        );

    public static Error LlmModelDisabled(LlmModelId modelId) =>
        Error.Conflict
        (
            code: "Chat.LlmModelDisabled",
            description: $"LLM model '{modelId.Value}' is disabled."
        );
}