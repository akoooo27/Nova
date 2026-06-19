using Chat.Application.Chats.Queries.GetChats;

namespace Chat.Api.Endpoints.Chats.GetChats;

internal static class ResponseMapper
{
    public static Response ToResponse(ChatListReadModel readModel) => new()
    {
        Items = readModel.Chats
            .Select(ToResponse)
            .ToList(),
        Total = readModel.Total,
        Limit = readModel.Limit,
        Offset = readModel.Offset
    };

    private static ChatListItemResponse ToResponse(ChatSummaryReadModel chat) => new()
    {
        Id = chat.Id,
        Title = chat.Title,
        IsPinned = chat.IsPinned,
        PinnedAt = chat.PinnedAt,
        IsArchived = chat.IsArchived,
        IsTemporary = chat.IsTemporary,
        CreatedAt = chat.CreatedAt,
        UpdatedAt = chat.UpdatedAt
    };
}