using Chat.Domain.Chats.ValueObjects;

using FluentValidation;

namespace Chat.Application.Chats.Commands.BranchChat;

internal sealed class BranchChatCommandValidator : AbstractValidator<BranchChatCommand>
{
    public BranchChatCommandValidator()
    {
        RuleFor(x => x.SourceChatId)
            .NotEmpty();

        RuleFor(x => x.SourceMessageId)
            .NotEmpty();

        RuleFor(x => x.Message)
            .NotEmpty()
            .MaximumLength(MessageContent.MaxLength);

        RuleFor(x => x.LlmModelId)
            .NotEmpty();
    }
}