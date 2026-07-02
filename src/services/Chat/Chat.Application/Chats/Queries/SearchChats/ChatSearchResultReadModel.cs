namespace Chat.Application.Chats.Queries.SearchChats;

public sealed record ChatSearchResultReadModel
(
    Guid Id,
    string Title,
    bool IsPinned,
    DateTimeOffset? PinnedAt,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int MatchCount,
    IReadOnlyList<ChatSearchSnippetReadModel> Snippets
);