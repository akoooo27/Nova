namespace Chat.Application.Chats.Queries.SearchChats;

public sealed record ChatSearchReadModel
(
    IReadOnlyList<ChatSearchResultReadModel> Chats,
    int Total,
    int Limit,
    int Offset
);