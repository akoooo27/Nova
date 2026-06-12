using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

namespace Chat.Application.Turns.Errors;

public static class TurnOperationErrors
{
    public static Error ModelNotConfigured(ChatMessageId messageId) =>
        Error.Conflict
        (
            code: "Turn.ModelNotConfigured",
            description: $"Assistant message '{messageId.Value}' has no model assigned."
        );

    public static Error ModelNotFound(LlmModelId modelId) =>
        Error.NotFound
        (
            code: "Turn.ModelNotFound",
            description: $"No model found with id '{modelId.Value}'."
        );
}