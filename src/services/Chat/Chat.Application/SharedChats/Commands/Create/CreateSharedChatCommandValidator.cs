using FluentValidation;

namespace Chat.Application.SharedChats.Commands.Create;

internal sealed class CreateSharedChatCommandValidator : AbstractValidator<CreateSharedChatCommand>
{
    public CreateSharedChatCommandValidator()
    {
        RuleFor(x => x.ChatId)
            .NotEmpty();

        RuleFor(x => x.CurrentMessageId)
            .NotEmpty();
    }
}