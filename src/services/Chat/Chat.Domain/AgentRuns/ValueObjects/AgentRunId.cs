using ErrorOr;

namespace Chat.Domain.AgentRuns.ValueObjects;

public sealed record AgentRunId
{
    public Guid Value { get; }

    private AgentRunId(Guid value)
    {
        Value = value;
    }

    public static AgentRunId New() => new(Guid.CreateVersion7());

    public static ErrorOr<AgentRunId> Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            return Error.Validation
            (
                code: "AgentRunId.Empty",
                description: "Agent run id cannot be empty."
            );
        }

        return new AgentRunId(value);
    }

    public static AgentRunId FromDatabase(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("Database contained an empty agent run id.");

        return new AgentRunId(value);
    }

    public override string ToString() => Value.ToString();
}