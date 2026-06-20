using Chat.Domain.Chats.ValueObjects;

using FluentValidation;

namespace Chat.Application.Chats.Commands.CreateChat;

internal sealed class CreateChatCommandValidator : AbstractValidator<CreateChatCommand>
{
    public CreateChatCommandValidator()
    {
        RuleFor(x => x.Message)
            .NotEmpty()
            .MaximumLength(MessageContent.MaxLength);

        RuleFor(x => x.LlmModelId)
            .NotEmpty();
        
        RuleFor(x => x.BranchingFromChatId)
            .NotEmpty()
            .When(x => x.BranchingFromChatId.HasValue || x.BranchingFromMessageId.HasValue);

        RuleFor(x => x.BranchingFromMessageId)
            .NotEmpty()
            .When(x => x.BranchingFromChatId.HasValue || x.BranchingFromMessageId.HasValue);
    }
}