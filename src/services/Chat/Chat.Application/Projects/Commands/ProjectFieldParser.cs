using Chat.Domain.Projects.ValueObjects;

using ErrorOr;

namespace Chat.Application.Projects.Commands;

internal static class ProjectFieldParser
{
    public static ProjectInstructions? ParseInstructions(string? value, List<Error> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        ErrorOr<ProjectInstructions> result = ProjectInstructions.Create(value);

        if (result.IsError)
        {
            errors.AddRange(result.Errors);
            return null;
        }

        return result.Value;
    }

    public static ProjectEmoji? ParseEmoji(string? value, List<Error> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        ErrorOr<ProjectEmoji> result = ProjectEmoji.Create(value);

        if (result.IsError)
        {
            errors.AddRange(result.Errors);
            return null;
        }

        return result.Value;
    }

    public static ProjectTheme? ParseTheme(string? value, List<Error> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        ErrorOr<ProjectTheme> result = ProjectTheme.Create(value);

        if (result.IsError)
        {
            errors.AddRange(result.Errors);
            return null;
        }

        return result.Value;
    }
}