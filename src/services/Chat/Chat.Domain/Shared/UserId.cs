using ErrorOr;

namespace Chat.Domain.Shared;

public sealed record UserId
{
    public string Value { get; }

    private UserId(string value)
    {
        Value = value;
    }

    public static ErrorOr<UserId> Create(string? value)
    {
        string? trimmed = value?.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "UserId.Required",
                description: "User id is required."
            );
        }

        return new UserId(trimmed);
    }

    public static UserId FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
            throw new DomainException("Database contained an invalid user id.");

        return new UserId(value);
    }

    public override string ToString() => Value;
}