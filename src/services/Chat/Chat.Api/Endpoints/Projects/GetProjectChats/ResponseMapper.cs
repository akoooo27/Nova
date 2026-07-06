using Chat.Application.Chats.Queries.GetChats;
using Chat.Application.Projects.Queries.GetProjectChats;

namespace Chat.Api.Endpoints.Projects.GetProjectChats;

internal static class ResponseMapper
{
    public static Response ToResponse(ProjectChatListReadModel readModel) => new()
    {
        Items = readModel.Chats
            .Select(ToResponse)
            .ToList(),
        Total = readModel.Total,
        Limit = readModel.Limit,
        Offset = readModel.Offset
    };

    private static ProjectChatResponse ToResponse(ChatSummaryReadModel chat) => new()
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