using FluentValidation;

namespace Chat.Application.Chats.Commands.RegenerateMessage;

internal sealed class RegenerateMessageCommandValidator : AbstractValidator<RegenerateMessageCommand>
{
    public RegenerateMessageCommandValidator()
    {
        RuleFor(x => x.ChatId)
            .NotEmpty();

        RuleFor(x => x.MessageId)
            .NotEmpty();

        RuleFor(x => x.ModelId)
            .NotEqual(Guid.Empty)
            .When(x => x.ModelId.HasValue);
    }
}