using Chat.Domain.SharedChats.ValueObjects;

using ErrorOr;

namespace Chat.Application.SharedChats.Errors;

public static class SharedChatOperationErrors
{
    public static Error SharedChatNotFound(SharedChatId sharedChatId) =>
        Error.NotFound
        (
            code: "SharedChatId.NotFound",
            description: $"No shared chat found with id '{sharedChatId.Value}'."
        );

    public static Error RemixNotAllowed(SharedChatId sharedChatId) =>
        Error.Forbidden
        (
            code: "SharedChat.RemixNotAllowed",
            description: $"Shared chat '{sharedChatId.Value}' does not permit remixing."
        );
}