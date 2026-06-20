namespace Chat.Application.Chats.Queries.GetChats;

public sealed record ChatListReadModel
(
    IReadOnlyList<ChatSummaryReadModel> Chats,
    int Total,
    int Limit,
    int Offset
);