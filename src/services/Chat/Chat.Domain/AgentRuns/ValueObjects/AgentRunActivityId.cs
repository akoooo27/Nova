using ErrorOr;

namespace Chat.Domain.AgentRuns.ValueObjects;

public sealed record AgentRunActivityId
{
    public Guid Value { get; }

    private AgentRunActivityId(Guid value)
    {
        Value = value;
    }

    public static AgentRunActivityId New() => new(Guid.CreateVersion7());

    public static ErrorOr<AgentRunActivityId> Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            return Error.Validation
            (
                code: "AgentRunActivityId.Empty",
                description: "Agent run activity id cannot be empty."
            );
        }

        return new AgentRunActivityId(value);
    }

    public static AgentRunActivityId FromDatabase(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("Database contained an empty agent run activity id.");

        return new AgentRunActivityId(value);
    }

    public override string ToString() => Value.ToString();
}