using FluentValidation;

namespace Chat.Application.Projects.Queries.ListProjects;

internal sealed class ListProjectsQueryValidator : AbstractValidator<ListProjectsQuery>
{
    public ListProjectsQueryValidator()
    {
        RuleFor(x => x.Limit)
            .InclusiveBetween(ProjectLimits.MinQueryLimit, ProjectLimits.MaxQueryLimit);

        RuleFor(x => x.Offset)
            .GreaterThanOrEqualTo(0);
    }
}