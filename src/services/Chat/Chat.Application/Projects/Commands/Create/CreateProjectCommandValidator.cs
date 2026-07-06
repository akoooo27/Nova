using Chat.Domain.Projects.ValueObjects;

using FluentValidation;

namespace Chat.Application.Projects.Commands.Create;

internal sealed class CreateProjectCommandValidator : AbstractValidator<CreateProjectCommand>
{
    public CreateProjectCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(ProjectName.MaxLength);

        RuleFor(x => x.Instructions)
            .MaximumLength(ProjectInstructions.MaxLength)
            .When(x => x.Instructions is not null);

        RuleFor(x => x.Emoji)
            .MaximumLength(ProjectEmoji.MaxLength)
            .When(x => x.Emoji is not null);
    }
}