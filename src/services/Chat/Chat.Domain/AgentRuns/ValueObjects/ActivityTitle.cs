using ErrorOr;

namespace Chat.Domain.AgentRuns.ValueObjects;

public sealed record ActivityTitle
{
    public const int MaxLength = 300;

    public string Value { get; }

    private ActivityTitle(string value)
    {
        Value = value;
    }

    public static ErrorOr<ActivityTitle> Create(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "ActivityTitle.Required",
                description: "Activity title is required."
            );
        }

        if (trimmed.Length > MaxLength)
        {
            return Error.Validation
            (
                code: "ActivityTitle.TooLong",
                description: $"Activity title cannot exceed {MaxLength} characters."
            );
        }

        return new ActivityTitle(trimmed);
    }

    public static ActivityTitle FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > MaxLength)
            throw new DomainException("Database contained an invalid activity title.");

        return new ActivityTitle(value);
    }

    public override string ToString() => Value;
}