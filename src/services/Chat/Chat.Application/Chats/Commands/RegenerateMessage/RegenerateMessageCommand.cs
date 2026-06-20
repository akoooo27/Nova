using Chat.Application.Chats.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.RegenerateMessage;

public sealed record RegenerateMessageCommand
(
    Guid ChatId,
    Guid MessageId,
    Guid? ModelId = null,
    bool ForceUseSearch = false
) : ICommand<ErrorOr<TurnStartedResult>>;