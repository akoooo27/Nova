using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;
using Chat.Domain.SharedChats.ValueObjects;

using SharedKernel;

namespace Chat.Domain.SharedChats;

public sealed class SharedChat : AggregateRoot<SharedChatId>
{
    public UserId UserId { get; private set; } = default!;

    public ChatId ChatId { get; private set; } = default!;

    public ChatMessageId CurrentMessageId { get; private set; } = default!;

    public ChatTitle Title { get; private set; } = default!;

    public DateTimeOffset CreatedAt { get; private set; }

    public bool AllowRemix { get; private set; }

    private SharedChat()
    {
        // EF Core materialization only
    }

    private SharedChat
    (
        SharedChatId id,
        UserId userId,
        ChatId chatId,
        ChatMessageId currentMessageId,
        ChatTitle title,
        DateTimeOffset createdAt,
        bool allowRemix
    ) : base(id)
    {
        UserId = userId;
        ChatId = chatId;
        CurrentMessageId = currentMessageId;
        Title = title;
        CreatedAt = createdAt;
        AllowRemix = allowRemix;
    }

    public static SharedChat Create
    (
        UserId userId,
        ChatId chatId,
        ChatMessageId currentMessageId,
        ChatTitle title,
        DateTimeOffset createdAt,
        bool allowRemix = false
    ) => new
    (
        id: SharedChatId.New(),
        userId: userId,
        chatId: chatId,
        currentMessageId: currentMessageId,
        title: title,
        createdAt: createdAt,
        allowRemix: allowRemix
    );
}