using ErrorOr;

using Mediator;

namespace Chat.Application.SharedChats.Queries.GetSharedChats;

public sealed record GetSharedChatsQuery(int Limit, int Offset)
    : IQuery<ErrorOr<SharedChatListReadModel>>;