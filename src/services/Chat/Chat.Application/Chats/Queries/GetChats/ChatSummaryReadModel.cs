namespace Chat.Application.Chats.Queries.GetChats;

public sealed record ChatSummaryReadModel
(
    Guid Id,
    string Title,
    bool IsPinned,
    DateTimeOffset? PinnedAt,
    bool IsArchived,
    bool IsTemporary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);