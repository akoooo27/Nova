using Chat.Domain.Projects.ValueObjects;

using ErrorOr;

namespace Chat.Application.Projects.Errors;


public static class ProjectOperationErrors
{
    public static Error ProjectNotFound(ProjectId projectId) =>
        Error.NotFound
        (
            code: "Project.NotFound",
            description: $"No project found with id '{projectId.Value}'."
        );

    public static Error TemporaryChatCannotJoinProject() =>
        Error.Validation
        (
            code: "Project.TemporaryChatCannotJoin",
            description: "A temporary chat cannot be created inside a project."
        );
}