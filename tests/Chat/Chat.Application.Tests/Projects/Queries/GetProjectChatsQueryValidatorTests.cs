using Chat.Application.Projects.Queries.GetProjectChats;

using FluentValidation.Results;

namespace Chat.Application.Tests.Projects.Queries;

public sealed class GetProjectChatsQueryValidatorTests
{
    private readonly GetProjectChatsQueryValidator _validator = new();

    [Fact]
    public void ValidateAcceptsValidQuery()
    {
        GetProjectChatsQuery query = new(ProjectId: Guid.CreateVersion7(), Limit: 20, Offset: 0);

        ValidationResult result = _validator.Validate(query);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateRejectsEmptyProjectId()
    {
        GetProjectChatsQuery query = new(ProjectId: Guid.Empty, Limit: 20, Offset: 0);

        ValidationResult result = _validator.Validate(query);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(GetProjectChatsQuery.ProjectId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void ValidateRejectsOutOfRangeLimit(int limit)
    {
        GetProjectChatsQuery query = new(ProjectId: Guid.CreateVersion7(), Limit: limit, Offset: 0);

        ValidationResult result = _validator.Validate(query);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(GetProjectChatsQuery.Limit));
    }

    [Fact]
    public void ValidateRejectsNegativeOffset()
    {
        GetProjectChatsQuery query = new(ProjectId: Guid.CreateVersion7(), Limit: 20, Offset: -1);

        ValidationResult result = _validator.Validate(query);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(GetProjectChatsQuery.Offset));
    }
}