using ErrorOr;

namespace Chat.Domain.Personalizations.ValueObjects;

public sealed record AboutUser
{
    public const int MaxLength = 1500;

    public string Value { get; }

    private AboutUser(string value)
    {
        Value = value;
    }

    public static ErrorOr<AboutUser> Create(string? value)
    {
        string? trimmed = value?.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "AboutUser.Required",
                description: "About user is required."
            );
        }

        if (trimmed.Length > MaxLength)
        {
            return Error.Validation
            (
                code: "AboutUser.TooLong",
                description: $"About user must be at most {MaxLength} characters long."
            );
        }

        return new AboutUser(trimmed);
    }

    public static AboutUser FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > MaxLength)
            throw new DomainException("Database contained an invalid about user.");

        return new AboutUser(value);
    }

    public override string ToString() => Value;
}