using ErrorOr;

namespace Chat.Domain.Projects.ValueObjects;

public sealed record ProjectInstructions
{
    public const int MaxLength = 8000;

    public string Value { get; }

    private ProjectInstructions(string value) => Value = value;

    public static ErrorOr<ProjectInstructions> Create(string? value)
    {
        string? trimmed = value?.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "ProjectInstructions.Required",
                description: "Project instructions are required."
            );
        }

        if (trimmed.Length > MaxLength)
        {
            return Error.Validation
            (
                code: "ProjectInstructions.TooLong",
                description: $"Project instructions must be at most {MaxLength} characters long."
            );
        }

        return new ProjectInstructions(trimmed);
    }

    public static ProjectInstructions FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > MaxLength)
            throw new DomainException("Database contained invalid project instructions.");

        return new ProjectInstructions(value);
    }

    public override string ToString() => Value;
}