using FluentValidation;

namespace Chat.Application.Chats.Commands.SetChatProject;

internal sealed class SetChatProjectCommandValidator : AbstractValidator<SetChatProjectCommand>
{
    public SetChatProjectCommandValidator()
    {
        RuleFor(x => x.ChatId)
            .NotEmpty();

        RuleFor(x => x.ProjectId!.Value)
            .NotEmpty()
            .When(x => x.ProjectId.HasValue);
    }
}