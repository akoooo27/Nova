using Chat.Application.Abstractions.Database;
using Chat.Application.Projects.Errors;
using Chat.Domain.Projects;
using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.Projects.Commands.Delete;

internal sealed class DeleteProjectHandler(
    IUserContext userContext,
    IProjectRepository projects,
    IUnitOfWork unitOfWork) : ICommandHandler<DeleteProjectCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(DeleteProjectCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ProjectId> projectIdResult = ProjectId.Create(command.ProjectId);

        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
        }

        if (projectIdResult.IsError)
        {
            errors.AddRange(projectIdResult.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        Project? project = await projects.GetByIdAsync(projectIdResult.Value, userIdResult.Value, cancellationToken);

        if (project is null)
        {
            return ProjectOperationErrors.ProjectNotFound(projectIdResult.Value);
        }

        projects.Remove(project);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}