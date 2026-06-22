using Chat.Application.SharedChats.Queries.GetSharedChats;

using FluentValidation.Results;

namespace Chat.Application.Tests.SharedChats;

public sealed class GetSharedChatsQueryValidatorTests
{
    private readonly GetSharedChatsQueryValidator _validator = new();

    [Fact]
    public void ValidateAcceptsInRangeLimitAndOffset()
    {
        ValidationResult result = _validator.Validate(new GetSharedChatsQuery(Limit: 50, Offset: 0));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void ValidateRejectsOutOfRangeLimit(int limit)
    {
        ValidationResult result = _validator.Validate(new GetSharedChatsQuery(Limit: limit, Offset: 0));

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(GetSharedChatsQuery.Limit));
    }

    [Fact]
    public void ValidateRejectsNegativeOffset()
    {
        ValidationResult result = _validator.Validate(new GetSharedChatsQuery(Limit: 50, Offset: -1));

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(GetSharedChatsQuery.Offset));
    }
}