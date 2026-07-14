using FluentValidation;

namespace Chat.Application.AgentRuns.Queries.GetAgentRun;

internal sealed class GetAgentRunQueryValidator : AbstractValidator<GetAgentRunQuery>
{
    public GetAgentRunQueryValidator()
    {
        RuleFor(x => x.ChatId)
            .NotEmpty();

        RuleFor(x => x.MessageId)
            .NotEmpty();
    }
}
