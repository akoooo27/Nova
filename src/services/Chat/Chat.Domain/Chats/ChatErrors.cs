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
}