namespace Chat.Api.Endpoints.Chats.GetChat;

internal sealed class MessageResponse
{
    public required string Role { get; init; }

    public required string? Content { get; init; }

    public required string Status { get; init; }

    public required string? FailureReason { get; init; }

    public required int SiblingIndex { get; init; }

    public required MessageModelResponse? Model { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset? CompletedAt { get; init; }

    public required string Kind { get; init; }

    public required AgentRunSummaryResponse? AgentRun { get; init; }
}

internal sealed class AgentRunSummaryResponse
{
    public required string Kind { get; init; }

    public required string? CurrentPhase { get; init; }

    public required IReadOnlyDictionary<string, int> ActivityCounts { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset? FinishedAt { get; init; }
}