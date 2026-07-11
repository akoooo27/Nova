using FluentValidation;

namespace Chat.Application.SharedChats.Commands.Remix;

internal sealed class RemixSharedChatCommandValidator : AbstractValidator<RemixSharedChatCommand>
{
    public RemixSharedChatCommandValidator()
    {
        RuleFor(x => x.ShareId)
            .NotEmpty();
    }
}