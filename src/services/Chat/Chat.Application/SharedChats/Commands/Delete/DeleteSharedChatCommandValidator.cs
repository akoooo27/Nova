using FluentValidation;

namespace Chat.Application.SharedChats.Commands.Delete;

internal sealed class DeleteSharedChatCommandValidator : AbstractValidator<DeleteSharedChatCommand>
{
    public DeleteSharedChatCommandValidator()
    {
        RuleFor(x => x.SharedChatId)
            .NotEmpty();
    }
}