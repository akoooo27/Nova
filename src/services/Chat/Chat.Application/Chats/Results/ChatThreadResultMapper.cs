using Chat.Domain.Chats;

namespace Chat.Application.Chats.Results;

public static class ChatThreadResultMapper
{
    public static ChatThreadResult ToResult(this ChatThread thread) => new
    (
        Id: thread.Id.Value,
        Title: thread.Title.Value,
        IsPinned: thread.IsPinned,
        PinnedAt: thread.PinnedAt,
        IsArchived: thread.IsArchived,
        IsTemporary: thread.IsTemporary,
        CreatedAt: thread.CreatedAt,
        UpdatedAt: thread.UpdatedAt
    );
}