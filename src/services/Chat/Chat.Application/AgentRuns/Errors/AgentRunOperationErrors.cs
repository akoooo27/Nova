using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

namespace Chat.Application.AgentRuns.Errors;

public static class AgentRunOperationErrors
{
    public static Error NotFound(ChatMessageId messageId) =>
        Error.NotFound
        (
            code: "AgentRun.NotFound",
            description: $"No agent run found for message '{messageId.Value}'."
        );

    public static Error ModelNotConfigured(ChatMessageId messageId) =>
        Error.Conflict
        (
            code: "AgentRun.ModelNotConfigured",
            description: $"Assistant message '{messageId.Value}' has no model assigned."
        );

    public static Error ModelNotFound(LlmModelId modelId) =>
        Error.NotFound
        (
            code: "AgentRun.ModelNotFound",
            description: $"No model found with id '{modelId.Value}'."
        );
}