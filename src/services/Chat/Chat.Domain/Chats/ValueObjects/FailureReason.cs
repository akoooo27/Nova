using ErrorOr;

namespace Chat.Domain.Chats.ValueObjects;

public sealed record FailureReason
{
    public const int MaxLength = 1024;

    public string Value { get; }

    private FailureReason(string value)
    {
        Value = value;
    }

    public static ErrorOr<FailureReason> Create(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "FailureReason.Required",
                description: "Failure reason is required."
            );
        }

        if (trimmed.Length > MaxLength)
        {
            return Error.Validation
            (
                code: "FailureReason.TooLong",
                description: $"Failure reason cannot exceed {MaxLength} characters."
            );
        }

        return new FailureReason(trimmed);
    }

    public static FailureReason FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
            throw new DomainException("Database contained an invalid failure reason.");

        return new FailureReason(value);
    }

    public override string ToString() => Value;
}