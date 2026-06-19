using Chat.Application.Chats.Results;

namespace Chat.Api.Endpoints.Chats.Responses;

internal sealed record ChatThreadResponse
(
    Guid Id,
    string Title,
    bool IsPinned,
    DateTimeOffset? PinnedAt,
    bool IsArchived,
    bool IsTemporary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
)
{
    public static ChatThreadResponse From(ChatThreadResult result) => new
    (
        Id: result.Id,
        Title: result.Title,
        IsPinned: result.IsPinned,
        PinnedAt: result.PinnedAt,
        IsArchived: result.IsArchived,
        IsTemporary: result.IsTemporary,
        CreatedAt: result.CreatedAt,
        UpdatedAt: result.UpdatedAt
    );
}