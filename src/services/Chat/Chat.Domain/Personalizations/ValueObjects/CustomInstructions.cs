using ErrorOr;

namespace Chat.Domain.Personalizations.ValueObjects;

public sealed record CustomInstructions
{
    public const int MaxLength = 1000;

    public string Value { get; }

    private CustomInstructions(string value)
    {
        Value = value;
    }

    public static ErrorOr<CustomInstructions> Create(string? value)
    {
        string? trimmed = value?.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "CustomInstructions.Required",
                description: "Custom instructions are required."
            );
        }

        if (trimmed.Length > MaxLength)
        {
            return Error.Validation
            (
                code: "CustomInstructions.TooLong",
                description: $"Custom instructions must be at most {MaxLength} characters long."
            );
        }

        return new CustomInstructions(trimmed);
    }

    public static CustomInstructions FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > MaxLength)
            throw new DomainException("Database contained an invalid custom instructions.");

        return new CustomInstructions(value);
    }

    public override string ToString() => Value;
}