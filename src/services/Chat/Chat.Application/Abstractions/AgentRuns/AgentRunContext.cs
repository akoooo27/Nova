using Chat.Application.Abstractions.Turns;
using Chat.Domain.AgentRuns.ValueObjects;

namespace Chat.Application.Abstractions.AgentRuns;

/// <summary>
/// Everything a runner needs to execute one agent run. <see cref="PriorConversation"/> holds the
/// completed messages on the active branch ABOVE the task user message (the task itself travels
/// separately as <see cref="Task"/>), oldest first.
/// </summary>
public sealed record AgentRunContext
(
    Guid RunId,
    Guid TurnId,
    Guid ChatId,
    string UserId,
    AgentRunKind Kind,
    string Task,
    string ExternalModelId,
    IReadOnlyList<TurnMessage> PriorConversation
);