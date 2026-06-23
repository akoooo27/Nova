using Chat.Application.Abstractions.Turns;
using Chat.Application.Chats.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.EditMessage;

public sealed record EditMessageCommand
(
    Guid ChatId,
    Guid MessageId,
    string Message,
    Guid LlmModelId,
    TurnGenerationOptions? GenerationOptions = null
) : ICommand<ErrorOr<TurnStartedResult>>;