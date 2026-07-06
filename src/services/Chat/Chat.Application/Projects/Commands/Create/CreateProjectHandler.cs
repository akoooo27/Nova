using Chat.Application.Abstractions.Database;
using Chat.Application.Projects.Results;
using Chat.Domain.Projects;
using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

using SharedKernel;

namespace Chat.Application.Projects.Commands.Create;

internal sealed class CreateProjectHandler(
    IUserContext userContext,
    IProjectRepository projects,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<CreateProjectCommand, ErrorOr<ProjectResult>>
{
    public async ValueTask<ErrorOr<ProjectResult>> Handle(CreateProjectCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ProjectName> nameResult = ProjectName.Create(command.Name);

        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
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

        UserId userId = userIdResult.Value;
        ProjectName name = nameResult.Value;

        Project project = Project.Create
        (
            userId: userId,
            name: name,
            instructions: instructions,
            emoji: emoji,
            theme: theme,
            createdAt: dateTimeProvider.UtcNow
        );

        projects.Add(project);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return project.ToResult();
    }
}