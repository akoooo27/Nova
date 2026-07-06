using Chat.Application.Chats.Queries.GetChats;

namespace Chat.Application.Projects.Queries.GetProjectChats;

public sealed record ProjectChatListReadModel
(
    IReadOnlyList<ChatSummaryReadModel> Chats,
    int Total,
    int Limit,
    int Offset
);