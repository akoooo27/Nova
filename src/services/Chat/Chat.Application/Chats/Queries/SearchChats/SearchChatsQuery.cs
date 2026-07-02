using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Queries.SearchChats;

public sealed record SearchChatsQuery
(
    string Query,
    bool IsArchived,
    int Limit,
    int Offset
) : IQuery<ErrorOr<ChatSearchReadModel>>;