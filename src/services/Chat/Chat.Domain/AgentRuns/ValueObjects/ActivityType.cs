using ErrorOr;

namespace Chat.Domain.AgentRuns.ValueObjects;

public sealed record ActivityType
{
    public const int MaxLength = 100;

    public string Value { get; }

    private ActivityType(string value)
    {
        Value = value;
    }

    public static ErrorOr<ActivityType> Create(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "ActivityType.Required",
                description: "Activity type is required."
            );
        }

        if (trimmed.Length > MaxLength)
        {
            return Error.Validation
            (
                code: "ActivityType.TooLong",
                description: $"Activity type cannot exceed {MaxLength} characters."
            );
        }

        if (!IsWellFormed(trimmed))
        {
            return Error.Validation
            (
                code: "ActivityType.InvalidFormat",
                description: "Activity type may only contain lowercase letters, digits, '.', '_' and '-'."
            );
        }

        return new ActivityType(trimmed);
    }

    public static ActivityType FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength || !IsWellFormed(value))
            throw new DomainException("Database contained an invalid activity type.");

        return new ActivityType(value);
    }

    private static bool IsWellFormed(string value) =>
        value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '_' or '-');

    public override string ToString() => Value;
}