using Chat.Domain.Projects.ValueObjects;

using FluentValidation;

namespace Chat.Application.Projects.Commands.Update;

internal sealed class UpdateProjectCommandValidator : AbstractValidator<UpdateProjectCommand>
{
    public UpdateProjectCommandValidator()
    {
        RuleFor(x => x.ProjectId)
            .NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(ProjectName.MaxLength);

        RuleFor(x => x.Instructions).
            MaximumLength(ProjectInstructions.MaxLength)
            .When(x => x.Instructions is not null);

        RuleFor(x => x.Emoji)
            .MaximumLength(ProjectEmoji.MaxLength)
            .When(x => x.Emoji is not null);
    }
}