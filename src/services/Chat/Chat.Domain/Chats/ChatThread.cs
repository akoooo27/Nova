using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using SharedKernel;

namespace Chat.Domain.Chats;

public sealed class ChatThread : AggregateRoot<ChatId>
{
    private readonly List<ChatMessage> _messages;

    public UserId UserId { get; private set; }

    public ChatTitle Title { get; private set; }

    public ChatMessageId CurrentMessageId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<ChatMessage> Messages => _messages;

    private ChatThread
    (
        ChatId id,
        UserId userId,
        ChatTitle title,
        ChatMessage root,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt
    ) : base(id)
    {
        UserId = userId;
        Title = title;
        CurrentMessageId = root.Id;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        _messages = [root];
    }

    public static ChatThread Create
    (
        UserId userId,
        ChatTitle title,
        MessageContent firstUserMessage,
        DateTimeOffset createdAt
    )
    {
        ChatId id = ChatId.New();

        ChatMessage root = ChatMessage.CreateUserMessage
        (
            chatId: id,
            parentMessageId: null,
            content: firstUserMessage,
            createdAt: createdAt,
            siblingIndex: SiblingIndex.First()
        );

        return new ChatThread
        (
            id: id,
            userId: userId,
            title: title,
            root: root,
            createdAt: createdAt,
            updatedAt: createdAt
        );
    }
}