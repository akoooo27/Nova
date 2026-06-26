using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;
using Chat.Domain.SharedChats;

namespace Chat.Domain.Tests.SharedChats;

public sealed class SharedChatTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 6, 9, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateMapsAllProvidedMetadata()
    {
        UserId userId = UserId.FromDatabase("auth0|user-1");
        ChatId chatId = ChatId.New();
        ChatMessageId currentMessageId = ChatMessageId.New();
        ChatTitle title = ChatTitle.FromDatabase("Planning chat");

        SharedChat shared = SharedChat.Create(userId, chatId, currentMessageId, title, CreatedAt);

        Assert.NotEqual(Guid.Empty, shared.Id.Value);
        Assert.Equal(userId, shared.UserId);
        Assert.Equal(chatId, shared.ChatId);
        Assert.Equal(currentMessageId, shared.CurrentMessageId);
        Assert.Equal(title, shared.Title);
        Assert.Equal(CreatedAt, shared.CreatedAt);
    }

    [Fact]
    public void CreateUsesRandomVersionFourIdAndIsUniquePerCall()
    {
        SharedChat first = SharedChat.Create
        (
            UserId.FromDatabase("auth0|user-1"),
            ChatId.New(),
            ChatMessageId.New(),
            ChatTitle.FromDatabase("Planning chat"),
            CreatedAt
        );

        SharedChat second = SharedChat.Create
        (
            UserId.FromDatabase("auth0|user-1"),
            ChatId.New(),
            ChatMessageId.New(),
            ChatTitle.FromDatabase("Planning chat"),
            CreatedAt
        );

        // Version 4 == random. A time-ordered v7 id would leak creation time and be
        // partially enumerable, which is unsafe for an anonymous share bearer token.
        Assert.Equal(4, first.Id.Value.Version);
        Assert.Equal(4, second.Id.Value.Version);
        Assert.NotEqual(first.Id, second.Id);
    }
}