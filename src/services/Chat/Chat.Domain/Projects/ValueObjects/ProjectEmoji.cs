using ErrorOr;

namespace Chat.Domain.Projects.ValueObjects;

public sealed record ProjectEmoji
{
    public const int MaxLength = 64;

    public string Value { get; }

    private ProjectEmoji(string value) => Value = value;

    public static ErrorOr<ProjectEmoji> Create(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "ProjectEmoji.Required",
                description: "Project emoji is required."
            );
        }

        if (trimmed.Length > MaxLength)
        {
            return Error.Validation
            (
                code: "ProjectEmoji.TooLong",
                description: $"Project emoji cannot exceed {MaxLength} characters."
            );
        }

        return new ProjectEmoji(trimmed);
    }

    public static ProjectEmoji FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
            throw new DomainException("Database contained an invalid project emoji.");

        return new ProjectEmoji(value);
    }

    public override string ToString() => Value;
}