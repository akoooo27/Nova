using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Queries.GetChat;

public sealed record GetChatQuery(Guid ChatId) : IQuery<ErrorOr<ChatDetailReadModel>>;