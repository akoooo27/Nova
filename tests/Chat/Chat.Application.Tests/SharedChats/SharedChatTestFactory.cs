using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Application.Tests.SharedChats;

internal static class SharedChatTestFactory
{
    public const string OwnerId = "auth0|user-1";

    /// <summary>
    /// Builds a chat whose current head is a completed assistant message rooted at a completed
    /// user message, so <see cref="ChatThread.ValidateShareAt"/> accepts the head node.
    /// </summary>
    public static ChatThread CreateShareableThread
    (
        DateTimeOffset createdAt,
        string userId = OwnerId,
        bool isTemporary = false,
        string title = "Source chat"
    )
    {
        ChatThread thread = ChatThread.Create
        (
            userId: UserId.FromDatabase(userId),
            title: ChatTitle.FromDatabase(title),
            firstUserMessage: MessageContent.FromDatabase("Original prompt"),
            createdAt: createdAt,
            isTemporary: isTemporary
        );

        ChatMessage assistant = thread.BeginAssistantMessage
        (
            parentMessageId: thread.CurrentMessageId,
            llmModelId: LlmModelId.New(),
            createdAt: createdAt.AddMinutes(1)
        ).Value;

        _ = thread.CompleteAssistantMessage
        (
            messageId: assistant.Id,
            content: MessageContent.FromDatabase("Original answer"),
            completedAt: createdAt.AddMinutes(2)
        );

        return thread;
    }
}