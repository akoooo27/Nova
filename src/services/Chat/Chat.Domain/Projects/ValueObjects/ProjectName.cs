using ErrorOr;

namespace Chat.Domain.Projects.ValueObjects;

public sealed record ProjectName
{
    public const int MaxLength = 120;

    public string Value { get; }

    private ProjectName(string value) => Value = value;

    public static ErrorOr<ProjectName> Create(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "ProjectName.Required",
                description: "Project name is required."
            );
        }

        if (trimmed.Length > MaxLength)
        {
            return Error.Validation
            (
                code: "ProjectName.TooLong",
                description: $"Project name cannot exceed {MaxLength} characters."
            );
        }

        return new ProjectName(trimmed);
    }

    public static ProjectName FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
            throw new DomainException("Database contained an invalid project name.");

        return new ProjectName(value);
    }

    public override string ToString() => Value;
}