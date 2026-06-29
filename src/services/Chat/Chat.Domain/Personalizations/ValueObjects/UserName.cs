using ErrorOr;

namespace Chat.Domain.Personalizations.ValueObjects;

public sealed record UserName
{
    public const int MaxLength = 100;

    public string Value { get; }

    private UserName(string value)
    {
        Value = value;
    }

    public static ErrorOr<UserName> Create(string? value)
    {
        string? trimmed = value?.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "UserName.Required",
                description: "User name is required."
            );
        }

        if (trimmed.Length > MaxLength)
        {
            return Error.Validation
            (
                code: "UserName.TooLong",
                description: $"User name must be at most {MaxLength} characters long."
            );
        }

        return new UserName(trimmed);
    }

    public static UserName FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > MaxLength)
            throw new DomainException("Database contained an invalid user name.");

        return new UserName(value);
    }

    public override string ToString() => Value;
}