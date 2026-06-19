namespace Chat.Application.Chats.Queries.GetChat;

public sealed record ChatDetailReadModel
(
    Guid Id,
    string Title,
    bool IsPinned,
    DateTimeOffset? PinnedAt,
    bool IsArchived,
    bool IsTemporary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid CurrentMessageId,
    IReadOnlyList<ChatMessageReadModel> Messages
);