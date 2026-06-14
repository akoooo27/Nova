using Chat.Application.Abstractions.Turns;
using Chat.Application.Chats.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.CreateChat;

public sealed record CreateChatCommand
(
    string Message,
    Guid LlmModelId,
    TurnGenerationOptions? GenerationOptions = null,
    bool IsTemporary = false
) : ICommand<ErrorOr<TurnStartedResult>>;