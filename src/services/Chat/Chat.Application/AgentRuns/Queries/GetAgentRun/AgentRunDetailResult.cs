namespace Chat.Application.AgentRuns.Queries.GetAgentRun;

public sealed record AgentRunActivityResult
(
    int Sequence,
    string Kind,
    string Type,
    string Title,
    string? Detail,
    DateTimeOffset OccurredAt
);

public sealed record AgentRunUsageResult(int InputTokens, int OutputTokens);

public sealed record AgentRunDetailResult
(
    string Kind,
    string Task,
    string? CurrentPhase,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    AgentRunUsageResult Usage,
    IReadOnlyList<AgentRunActivityResult> Activities
);
