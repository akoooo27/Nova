namespace Chat.Application.SharedChats.Queries.GetSharedChats;

public sealed record SharedChatListReadModel
(
    IReadOnlyList<SharedChatSummaryReadModel> SharedChats,
    int Total,
    int Limit,
    int Offset
);