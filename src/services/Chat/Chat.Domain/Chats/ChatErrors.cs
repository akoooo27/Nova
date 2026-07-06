using Chat.Domain.Chats.ValueObjects;

using ErrorOr;

namespace Chat.Domain.Chats;

public static class ChatErrors
{
    public static Error CannotCompleteNonGenerating(ChatMessageId messageId) =>
        Error.Conflict
        (
            code: "Chat.CannotCompleteNonGenerating",
            description: $"Message '{messageId.Value}' is not generating and cannot be completed."
        );

    public static Error CannotFailNonGenerating(ChatMessageId messageId) =>
        Error.Conflict
        (
            code: "Chat.CannotFailNonGenerating",
            description: $"Message '{messageId.Value}' is not generating and cannot be failed."
        );

    public static Error ParentMessageNotFound(ChatMessageId messageId) =>
        Error.NotFound
        (
            code: "Chat.ParentMessageNotFound",
            description: $"No parent message found with id '{messageId.Value}'."
        );

    public static Error UserParentMustBeAssistant(ChatMessageId parentMessageId) =>
        Error.Conflict
        (
            code: "Chat.UserParentMustBeAssistant",
            description:
            $"User messages may only follow an assistant message; '{parentMessageId.Value}' is not an assistant message."
        );

    public static Error AssistantParentMustBeUser(ChatMessageId parentMessageId) =>
        Error.Conflict
        (
            code: "Chat.AssistantParentMustBeUser",
            description:
            $"Assistant messages must reply to a user message; '{parentMessageId.Value}' is not a user message."
        );

    public static Error MessageNotFound(ChatMessageId messageId) =>
        Error.NotFound
        (
            code: "Chat.MessageNotFound",
            description: $"No message found with id '{messageId.Value}'."
        );

    public static Error EditTargetMustBeUser(ChatMessageId messageId) =>
        Error.Conflict
        (
            code: "Chat.EditTargetMustBeUser",
            description: $"Only user messages can be edited; '{messageId.Value}' is not a user message."
        );

    public static Error RegenerationTargetMustBeAssistant(ChatMessageId messageId) =>
        Error.Conflict
        (
            code: "Chat.RegenerationTargetMustBeAssistant",
            description: $"Only assistant messages can be regenerated; '{messageId.Value}' is not an assistant message."
        );

    public static Error CannotRegenerateWhileGenerating(ChatMessageId messageId) =>
        Error.Conflict
        (
            code: "Chat.CannotRegenerateWhileGenerating",
            description: $"Message '{messageId.Value}' is still generating and cannot be regenerated yet."
        );

    public static Error ParentStillGenerating(ChatMessageId parentMessageId) =>
        Error.Conflict
        (
            code: "Chat.ParentStillGenerating",
            description:
            $"Message '{parentMessageId.Value}' is still generating; wait for the turn to finish before replying."
        );

    public static Error CannotBranchTemporaryChat(ChatId chatId) =>
        Error.Conflict
        (
            code: "Chat.CannotBranchTemporaryChat",
            description: $"Temporary chat '{chatId.Value}' cannot be branched into a new chat."
        );

    public static Error BranchPointMustBeAssistant(ChatMessageId messageId) =>
        Error.Conflict
        (
            code: "Chat.BranchPointMustBeAssistant",
            description:
            $"Only an assistant message can be used as a branch point; '{messageId.Value}' is not an assistant message."
        );

    public static Error CannotBranchWhileGenerating(ChatMessageId messageId) =>
        Error.Conflict
        (
            code: "Chat.CannotBranchWhileGenerating",
            description: $"Message '{messageId.Value}' is still generating and cannot be branched yet."
        );

    public static Error InvalidBranchPath(ChatMessageId messageId) =>
        Error.Unexpected
        (
            code: "Chat.InvalidBranchPath",
            description: $"The persisted ancestry for message '{messageId.Value}' is invalid."
        );

    public static Error CannotShareTemporaryChat(ChatId chatId) =>
        Error.Conflict
        (
            code: "Chat.CannotShareTemporaryChat",
            description: $"Temporary chat '{chatId.Value}' cannot be shared."
        );

    public static Error CannotShareGeneratingMessage(ChatMessageId messageId) =>
        Error.Conflict
        (
            code: "Chat.CannotShareGeneratingMessage",
            description: $"Generating message '{messageId.Value}' cannot be shared."
        );

    public static Error InvalidSharePath(ChatMessageId messageId) =>
        Error.Unexpected
        (
            code: "Chat.InvalidSharePath",
            description: $"The persisted ancestry for shared message '{messageId.Value}' is invalid."
        );

    public static Error EditTargetNotOnActivePath(ChatMessageId messageId) =>
        Error.Conflict
        (
            code: "Chat.EditTargetNotOnActivePath",
            description:
            $"User message '{messageId.Value}' is not on the active conversation path and cannot be edited."
        );

    public static Error CannotEditWhileGenerating(ChatMessageId messageId) =>
        Error.Conflict
        (
            code: "Chat.CannotEditWhileGenerating",
            description:
            $"Active-path assistant message '{messageId.Value}' is still generating; wait for the turn to finish before editing."
        );

    public static Error StopTargetMustBeAssistant(ChatMessageId messageId) =>
        Error.Conflict
        (
            code: "Chat.StopTargetMustBeAssistant",
            description: $"Only assistant messages can be stopped; '{messageId.Value}' is not an assistant message."
        );

    public static Error CannotStopNonGenerating(ChatMessageId messageId) =>
        Error.Conflict
        (
            code: "Chat.CannotStopNonGenerating",
            description: $"Message '{messageId.Value}' is not generating and cannot be stopped."
        );

    public static Error CannotAddTemporaryChatToProject(ChatId chatId) =>
        Error.Conflict
        (
            code: "Chat.CannotAddTemporaryChatToProject",
            description: $"Temporary chat '{chatId.Value}' cannot be added to a project."
        );
}