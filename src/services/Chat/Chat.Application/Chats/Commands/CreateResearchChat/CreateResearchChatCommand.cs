using Chat.Application.Chats.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.CreateResearchChat;

public sealed record CreateResearchChatCommand
(
    string Task,
    Guid LlmModelId,
    Guid? ProjectId = null
) : ICommand<ErrorOr<TurnStartedResult>>;