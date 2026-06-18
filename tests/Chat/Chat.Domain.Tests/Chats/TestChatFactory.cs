using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Domain.Tests.Chats;

internal static class TestChatFactory
{
    public static readonly DateTimeOffset CreatedAt = new(2026, 6, 9, 10, 0, 0, TimeSpan.Zero);

    public static ChatThread CreateThread(bool isTemporary = false)
    {
        return ChatThread.Create
        (
            userId: CreateUserId(),
            title: CreateTitle(),
            firstUserMessage: CreateContent("Hello"),
            createdAt: CreatedAt,
            isTemporary: isTemporary
        );
    }

    public static UserId CreateUserId(string value = "auth0|user-1") => UserId.FromDatabase(value);

    public static ChatTitle CreateTitle(string value = "Planning chat") => ChatTitle.FromDatabase(value);

    public static MessageContent CreateContent(string value = "Message") => MessageContent.FromDatabase(value);

    public static FailureReason CreateFailureReason(string value = "Provider failed") => FailureReason.FromDatabase(value);

    public static ChatMessage RootMessage(ChatThread chat) => Assert.Single(chat.Messages);
}