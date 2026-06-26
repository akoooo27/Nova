using ErrorOr;

using Mediator;

namespace Chat.Application.SharedChats.Queries.GetPublicSharedChat;

public sealed record GetPublicSharedChatQuery(Guid SharedChatId)
    : IQuery<ErrorOr<PublicSharedChatReadModel>>;