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
}