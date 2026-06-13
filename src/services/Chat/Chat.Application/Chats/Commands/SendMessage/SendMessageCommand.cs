using Chat.Application.Chats.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.SendMessage;

public sealed record SendMessageCommand
(
    Guid ChatId,
    string Message,
    Guid LlmModelId
) : ICommand<ErrorOr<TurnStartedResult>>;