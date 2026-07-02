using ErrorOr;

namespace Chat.Domain.Personalizations.ValueObjects;

public sealed record UserRole
{
    public const int MaxLength = 100;

    public string Value { get; }

    private UserRole(string value)
    {
        Value = value;
    }

    public static ErrorOr<UserRole> Create(string? value)
    {
        string? trimmed = value?.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "UserRole.Required",
                description: "User role is required."
            );
        }

        if (trimmed.Length > MaxLength)
        {
            return Error.Validation
            (
                code: "UserRole.TooLong",
                description: $"User role must be at most {MaxLength} characters long."
            );
        }

        return new UserRole(trimmed);
    }

    public static UserRole FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > MaxLength)
            throw new DomainException("Database contained an invalid user role.");

        return new UserRole(value);
    }

    public override string ToString() => Value;
}