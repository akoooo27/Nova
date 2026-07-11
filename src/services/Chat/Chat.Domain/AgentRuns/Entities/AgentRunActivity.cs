using Chat.Domain.AgentRuns.ValueObjects;

using SharedKernel;

namespace Chat.Domain.AgentRuns.Entities;

public sealed class AgentRunActivity : Entity<AgentRunActivityId>
{
    public AgentRunId RunId { get; private set; } = default!;

    public ActivitySequence Sequence { get; private set; } = default!;

    public ActivityKind Kind { get; private set; } = default!;

    public ActivityType Type { get; private set; } = default!;

    public ActivityTitle Title { get; private set; } = default!;

    public ActivityDetail? Detail { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    private AgentRunActivity()
    {
        // EF Core materialization only
    }

    private AgentRunActivity
    (
        AgentRunActivityId id,
        AgentRunId runId,
        ActivitySequence sequence,
        ActivityKind kind,
        ActivityType type,
        ActivityTitle title,
        ActivityDetail? detail,
        DateTimeOffset occurredAt
    ) : base(id)
    {
        RunId = runId;
        Sequence = sequence;
        Kind = kind;
        Type = type;
        Title = title;
        Detail = detail;
        OccurredAt = occurredAt;
    }

    internal static AgentRunActivity Create
    (
        AgentRunId runId,
        ActivitySequence sequence,
        ActivityKind kind,
        ActivityType type,
        ActivityTitle title,
        ActivityDetail? detail,
        DateTimeOffset occurredAt
    ) => new
    (
        id: AgentRunActivityId.New(),
        runId: runId,
        sequence: sequence,
        kind: kind,
        type: type,
        title: title,
        detail: detail,
        occurredAt: occurredAt
    );
}