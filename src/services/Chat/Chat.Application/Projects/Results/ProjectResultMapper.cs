using Chat.Domain.Projects;

namespace Chat.Application.Projects.Results;

internal static class ProjectResultMapper
{
    public static ProjectResult ToResult(this Project project) => new
    (
        Id: project.Id.Value,
        Name: project.Name.Value,
        Instructions: project.Instructions?.Value,
        Emoji: project.Emoji?.Value,
        Theme: project.Theme?.Value,
        CreatedAt: project.CreatedAt,
        UpdatedAt: project.UpdatedAt
    );
}