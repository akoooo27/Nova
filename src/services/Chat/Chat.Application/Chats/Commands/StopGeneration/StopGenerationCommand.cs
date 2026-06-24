using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.StopGeneration;

public sealed record StopGenerationCommand
(
    Guid ChatId,
    Guid AssistantMessageId
) : ICommand<ErrorOr<Success>>;