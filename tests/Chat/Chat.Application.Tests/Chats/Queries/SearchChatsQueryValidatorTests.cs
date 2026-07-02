using Chat.Application.Chats;
using Chat.Application.Chats.Queries.SearchChats;

using FluentValidation.Results;

namespace Chat.Application.Tests.Chats.Queries;

public sealed class SearchChatsQueryValidatorTests
{
    private readonly SearchChatsQueryValidator _validator = new();

    [Fact]
    public void ValidateAcceptsValidQuery()
    {
        SearchChatsQuery query = new(Query: "memory bug", IsArchived: false, Limit: 20, Offset: 0);

        ValidationResult result = _validator.Validate(query);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateRejectsBlankQuery(string text)
    {
        SearchChatsQuery query = new(Query: text, IsArchived: false, Limit: 20, Offset: 0);

        ValidationResult result = _validator.Validate(query);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(SearchChatsQuery.Query));
    }

    [Fact]
    public void ValidateRejectsOverlongQuery()
    {
        string text = new('a', ChatLimits.MaxSearchQueryLength + 1);
        SearchChatsQuery query = new(Query: text, IsArchived: false, Limit: 20, Offset: 0);

        ValidationResult result = _validator.Validate(query);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(SearchChatsQuery.Query));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void ValidateRejectsOutOfRangeLimit(int limit)
    {
        SearchChatsQuery query = new(Query: "memory", IsArchived: false, Limit: limit, Offset: 0);

        ValidationResult result = _validator.Validate(query);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(SearchChatsQuery.Limit));
    }

    [Fact]
    public void ValidateRejectsNegativeOffset()
    {
        SearchChatsQuery query = new(Query: "memory", IsArchived: false, Limit: 20, Offset: -1);

        ValidationResult result = _validator.Validate(query);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(SearchChatsQuery.Offset));
    }
}