using Chat.Domain.Chats.ValueObjects;

using FluentValidation;

namespace Chat.Application.Chats.Commands.UpdateChat;

internal sealed class UpdateChatCommandValidator : AbstractValidator<UpdateChatCommand>
{
    public UpdateChatCommandValidator()
    {
        RuleFor(x => x.ChatId)
            .NotEmpty();

        RuleFor(x => x.Title)
            .NotEmpty()
            .MaximumLength(ChatTitle.MaxLength);
    }
}