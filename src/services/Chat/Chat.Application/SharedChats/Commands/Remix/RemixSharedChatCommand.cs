using Chat.Application.SharedChats.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.SharedChats.Commands.Remix;

public sealed record RemixSharedChatCommand(Guid ShareId) : ICommand<ErrorOr<RemixSharedChatResult>>;