using System.Data;

using Chat.Domain.AgentRuns.ValueObjects;

using FluentValidation;

namespace Chat.Application.Chats.Commands.StartResearch;

internal sealed class StartResearchCommandValidator : AbstractValidator<StartResearchCommand>
{
    public StartResearchCommandValidator()
    {
        RuleFor(x => x.ChatId)
            .NotEmpty();

        RuleFor(x => x.Task)
            .NotEmpty()
            .MaximumLength(AgentTask.MaxLength);

        RuleFor(x => x.LlmModelId)
            .NotEmpty();
    }
}