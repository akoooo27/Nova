using Chat.Application.Chats.Queries.GetChats;

using FluentValidation.Results;

namespace Chat.Application.Tests.Chats.Queries;

public sealed class GetChatsQueryValidatorTests
{
    private readonly GetChatsQueryValidator _validator = new();

    [Fact]
    public void ValidateAcceptsInRangeLimitAndOffset()
    {
        GetChatsQuery query = new(IsArchived: false, Limit: 20, Offset: 0);

        ValidationResult result = _validator.Validate(query);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void ValidateRejectsOutOfRangeLimit(int limit)
    {
        GetChatsQuery query = new(IsArchived: false, Limit: limit, Offset: 0);

        ValidationResult result = _validator.Validate(query);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(GetChatsQuery.Limit));
    }

    [Fact]
    public void ValidateRejectsNegativeOffset()
    {
        GetChatsQuery query = new(IsArchived: false, Limit: 20, Offset: -1);

        ValidationResult result = _validator.Validate(query);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(GetChatsQuery.Offset));
    }
}