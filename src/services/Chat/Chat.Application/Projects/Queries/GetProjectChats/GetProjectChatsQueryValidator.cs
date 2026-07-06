using FluentValidation;

namespace Chat.Application.Projects.Queries.GetProjectChats;

internal sealed class GetProjectChatsQueryValidator : AbstractValidator<GetProjectChatsQuery>
{
    public GetProjectChatsQueryValidator()
    {
        RuleFor(x => x.ProjectId)
            .NotEmpty();

        RuleFor(x => x.Limit)
            .InclusiveBetween(ProjectLimits.MinQueryLimit, ProjectLimits.MaxQueryLimit);

        RuleFor(x => x.Offset)
            .GreaterThanOrEqualTo(0);
    }
}