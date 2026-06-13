using Chat.Domain.Chats.ValueObjects;

using FluentValidation;

namespace Chat.Application.Chats.Commands.SendMessage;

internal sealed class SendMessageCommandValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageCommandValidator()
    {
        RuleFor(x => x.ChatId)
            .NotEmpty();

        RuleFor(x => x.Message)
            .NotEmpty()
            .MaximumLength(MessageContent.MaxLength);

        RuleFor(x => x.LlmModelId)
            .NotEmpty();
    }
}