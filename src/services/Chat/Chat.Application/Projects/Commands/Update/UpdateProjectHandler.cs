using Chat.Application.Abstractions.Database;
using Chat.Application.Projects.Errors;
using Chat.Application.Projects.Results;
using Chat.Domain.Projects;
using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

using SharedKernel;

namespace Chat.Application.Projects.Commands.Update;

internal sealed class UpdateProjectHandler(
    IUserContext userContext,
    IProjectRepository projects,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<UpdateProjectCommand, ErrorOr<ProjectResult>>
{
    public async ValueTask<ErrorOr<ProjectResult>> Handle(UpdateProjectCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ProjectId> projectIdResult = ProjectId.Create(command.ProjectId);
        ErrorOr<ProjectName> nameResult = ProjectName.Create(command.Name);

        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
        }

        if (projectIdResult.IsError)
        {
            errors.AddRange(projectIdResult.Errors);
        }

        if (nameResult.IsError)
        {
            errors.AddRange(nameResult.Errors);
        }

        ProjectInstructions? instructions = ProjectFieldParser.ParseInstructions(command.Instructions, errors);
        ProjectEmoji? emoji = ProjectFieldParser.ParseEmoji(command.Emoji, errors);
        ProjectTheme? theme = ProjectFieldParser.ParseTheme(command.Theme, errors);

        if (errors.Count > 0)
        {
            return errors;
        }

        ProjectName name = nameResult.Value;

        Project? project = await projects.GetByIdAsync(projectIdResult.Value, userIdResult.Value, cancellationToken);

        if (project is null)
        {
            return ProjectOperationErrors.ProjectNotFound(projectIdResult.Value);
        }

        DateTimeOffset now = dateTimeProvider.UtcNow;

        project.Rename(name, now);
        project.UpdateInstructions(instructions, now);
        project.UpdateAppearance(emoji, theme, now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return project.ToResult();
    }
}