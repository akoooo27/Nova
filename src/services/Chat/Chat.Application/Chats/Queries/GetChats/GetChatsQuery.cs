using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Queries.GetChats;

public sealed record GetChatsQuery(int Limit, int Offset) : IQuery<ErrorOr<ChatListReadModel>>;