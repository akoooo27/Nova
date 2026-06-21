using Chat.Application.SharedChats.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.SharedChats.Commands.Create;

public sealed record CreateSharedChatCommand(Guid ChatId, Guid CurrentMessageId) : ICommand<ErrorOr<SharedChatResult>>;