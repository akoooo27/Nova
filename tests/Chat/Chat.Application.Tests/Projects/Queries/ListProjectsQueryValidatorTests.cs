using Chat.Application.Projects.Queries.ListProjects;

using FluentValidation.Results;

namespace Chat.Application.Tests.Projects.Queries;

public sealed class ListProjectsQueryValidatorTests
{
    private readonly ListProjectsQueryValidator _validator = new();

    [Fact]
    public void ValidateAcceptsInRangeLimitAndOffset()
    {
        ListProjectsQuery query = new(Limit: 20, Offset: 0);

        ValidationResult result = _validator.Validate(query);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void ValidateRejectsOutOfRangeLimit(int limit)
    {
        ListProjectsQuery query = new(Limit: limit, Offset: 0);

        ValidationResult result = _validator.Validate(query);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(ListProjectsQuery.Limit));
    }

    [Fact]
    public void ValidateRejectsNegativeOffset()
    {
        ListProjectsQuery query = new(Limit: 20, Offset: -1);

        ValidationResult result = _validator.Validate(query);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(ListProjectsQuery.Offset));
    }
}