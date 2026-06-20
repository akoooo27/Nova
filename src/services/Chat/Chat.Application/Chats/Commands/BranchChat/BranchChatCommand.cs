using Chat.Application.Abstractions.Turns;
using Chat.Application.Chats.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.BranchChat;

public sealed record BranchChatCommand
(
    Guid SourceChatId,
    Guid SourceMessageId,
    string Message,
    Guid LlmModelId,
    TurnGenerationOptions? GenerationOptions = null
) : ICommand<ErrorOr<TurnStartedResult>>;