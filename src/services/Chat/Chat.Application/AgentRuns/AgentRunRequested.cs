namespace Chat.Application.AgentRuns;

public sealed record AgentRunRequested
(
    Guid ChatId,
    string UserId,
    Guid AssistantMessageId,
    Guid RunId
);