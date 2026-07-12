using Chat.Application.Chats.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.AgentRuns.Commands.StartResearch;

public sealed record StartResearchCommand
(
    Guid ChatId,
    string Task,
    Guid LlmModelId
) : ICommand<ErrorOr<TurnStartedResult>>;