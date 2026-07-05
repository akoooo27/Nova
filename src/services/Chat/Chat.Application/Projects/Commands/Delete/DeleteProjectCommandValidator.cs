using FluentValidation;

namespace Chat.Application.Projects.Commands.Delete;

internal sealed class DeleteProjectCommandValidator : AbstractValidator<DeleteProjectCommand>
{
    public DeleteProjectCommandValidator()
    {
        RuleFor(x => x.ProjectId)
            .NotEmpty();
    }
}