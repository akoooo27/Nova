namespace Chat.Application.Chats.Queries.GetChat;

/// <summary>
/// Compact agent-run card summary, fully DERIVED at read time (never stored):
/// counts group activities by their kind-owned ActivityType (e.g. "web.search": 12, "source": 8);
/// CurrentPhase is the latest Phase activity's title. Null on branched/remixed copies without a run.
/// </summary>
public sealed record AgentRunSummaryReadModel
(
    string Kind,
    string? CurrentPhase,
    IReadOnlyDictionary<string, int> ActivityCounts,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt
);