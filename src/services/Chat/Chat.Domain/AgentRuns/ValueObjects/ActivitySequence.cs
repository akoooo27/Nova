using System.Globalization;

using ErrorOr;

namespace Chat.Domain.AgentRuns.ValueObjects;

public sealed record ActivitySequence : IComparable<ActivitySequence>
{
    public int Value { get; }

    private ActivitySequence(int value)
    {
        Value = value;
    }

    public static ErrorOr<ActivitySequence> Create(int value)
    {
        if (value <= 0)
        {
            return Error.Validation
            (
                code: "ActivitySequence.NotPositive",
                description: "Activity sequence must be a positive integer."
            );
        }

        return new ActivitySequence(value);
    }

    public static ActivitySequence FromDatabase(int value)
    {
        if (value <= 0)
            throw new DomainException("Database contained a non-positive activity sequence.");

        return new ActivitySequence(value);
    }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    public int ToInt() => Value;

    public int CompareTo(ActivitySequence? other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Value.CompareTo(other.Value);
    }

    public static bool operator >(ActivitySequence left, ActivitySequence right) => left.CompareTo(right) > 0;

    public static bool operator >=(ActivitySequence left, ActivitySequence right) => left.CompareTo(right) >= 0;

    public static bool operator <(ActivitySequence left, ActivitySequence right) => left.CompareTo(right) < 0;

    public static bool operator <=(ActivitySequence left, ActivitySequence right) => left.CompareTo(right) <= 0;
}