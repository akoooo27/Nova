using FluentValidation;

namespace Chat.Application.Chats.Commands.DeleteChat;

internal sealed class DeleteChatCommandValidator : AbstractValidator<DeleteChatCommand>
{
    public DeleteChatCommandValidator()
    {
        RuleFor(x => x.ChatId)
            .NotEmpty();
    }
}