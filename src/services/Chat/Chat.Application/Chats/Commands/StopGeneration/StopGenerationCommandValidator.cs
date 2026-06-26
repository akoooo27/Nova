using FluentValidation;

namespace Chat.Application.Chats.Commands.StopGeneration;

internal sealed class StopGenerationCommandValidator : AbstractValidator<StopGenerationCommand>
{
    public StopGenerationCommandValidator()
    {
        RuleFor(x => x.ChatId)
            .NotEmpty();

        RuleFor(x => x.AssistantMessageId)
            .NotEmpty();
    }
}