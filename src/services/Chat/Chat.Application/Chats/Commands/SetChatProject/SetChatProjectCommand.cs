using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.SetChatProject;

public sealed record SetChatProjectCommand
(
    Guid ChatId,
    Guid? ProjectId
) : ICommand<ErrorOr<Success>>;