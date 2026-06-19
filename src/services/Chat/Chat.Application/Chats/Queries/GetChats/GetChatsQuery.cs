using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Queries.GetChats;

public sealed record GetChatsQuery
(
    bool IsArchived,
    int Limit,
    int Offset
) : IQuery<ErrorOr<ChatListReadModel>>;