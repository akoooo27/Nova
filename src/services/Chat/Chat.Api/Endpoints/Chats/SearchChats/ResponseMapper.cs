using Chat.Application.Chats.Queries.SearchChats;

namespace Chat.Api.Endpoints.Chats.SearchChats;

internal static class ResponseMapper
{
    public static Response ToResponse(ChatSearchReadModel readModel) => new()
    {
        Items = readModel.Chats
            .Select(ToResponse)
            .ToList(),
        Total = readModel.Total,
        Limit = readModel.Limit,
        Offset = readModel.Offset
    };

    private static SearchChatResultResponse ToResponse(ChatSearchResultReadModel chat) => new()
    {
        Id = chat.Id,
        Title = chat.Title,
        IsPinned = chat.IsPinned,
        PinnedAt = chat.PinnedAt,
        IsArchived = chat.IsArchived,
        CreatedAt = chat.CreatedAt,
        UpdatedAt = chat.UpdatedAt,
        MatchCount = chat.MatchCount,
        Snippets = chat.Snippets
            .Select(ToResponse)
            .ToList()
    };

    private static SearchChatSnippetResponse ToResponse(ChatSearchSnippetReadModel snippet) => new()
    {
        MessageId = snippet.MessageId,
        Role = snippet.Role,
        Text = snippet.Text
    };
}