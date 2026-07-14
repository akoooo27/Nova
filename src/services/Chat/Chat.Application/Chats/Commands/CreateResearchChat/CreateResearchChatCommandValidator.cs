using Chat.Domain.AgentRuns.ValueObjects;

using FluentValidation;

namespace Chat.Application.Chats.Commands.CreateResearchChat;

internal sealed class CreateResearchChatCommandValidator : AbstractValidator<CreateResearchChatCommand>
{
    public CreateResearchChatCommandValidator()
    {
        RuleFor(x => x.Task)
            .NotEmpty()
            .MaximumLength(AgentTask.MaxLength);

        RuleFor(x => x.LlmModelId)
            .NotEmpty();

        RuleFor(x => x.ProjectId)
            .NotEmpty()
            .When(x => x.ProjectId is not null);
    }
}