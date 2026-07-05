using System.Text.RegularExpressions;

using ErrorOr;

namespace Chat.Domain.Projects.ValueObjects;

public sealed partial record ProjectTheme
{
    public string Value { get; }

    private ProjectTheme(string value) => Value = value;

    public static ErrorOr<ProjectTheme> Create(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "ProjectTheme.Required",
                description: "Project theme is required."
            );
        }

        if (!HexColor().IsMatch(trimmed))
        {
            return Error.Validation
            (
                code: "ProjectTheme.Invalid",
                description: "Project theme must be a hex color like #F6C543."
            );
        }

        return new ProjectTheme($"#{trimmed.TrimStart('#').ToUpperInvariant()}");
    }

    public static ProjectTheme FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !HexColor().IsMatch(value))
            throw new DomainException("Database contained an invalid project theme.");

        return new ProjectTheme(value);
    }

    [GeneratedRegex("^#?[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();

    public override string ToString() => Value;
}