using Chat.Domain;
using Chat.Domain.AgentRuns.ValueObjects;

using ErrorOr;

namespace Chat.Domain.Tests.AgentRuns.ValueObjects;

public sealed class AgentRunStringValueObjectTests
{
    [Fact]
    public void ActivityTitleCreateTrimsWhitespaceWhenValueIsValid()
    {
        ErrorOr<ActivityTitle> result = ActivityTitle.Create("  Planning  ");

        Assert.False(result.IsError);
        Assert.Equal("Planning", result.Value.Value);
        Assert.Equal("Planning", result.Value.ToString());
    }

    [Fact]
    public void ActivityTitleCreateReturnsRequiredValidationWhenValueIsBlank()
    {
        AssertError(ActivityTitle.Create("   "), "ActivityTitle.Required");
    }

    [Fact]
    public void ActivityTitleCreateReturnsTooLongValidationWhenValueExceedsMaxLength()
    {
        AssertError(ActivityTitle.Create(new string('a', ActivityTitle.MaxLength + 1)), "ActivityTitle.TooLong");
    }

    [Fact]
    public void ActivityTitleCreateAcceptsValueAtMaxLength()
    {
        ErrorOr<ActivityTitle> result = ActivityTitle.Create(new string('a', ActivityTitle.MaxLength));

        Assert.False(result.IsError);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" Planning ")]
    public void ActivityTitleFromDatabaseThrowsDomainExceptionWhenValueIsBlankOrUntrimmed(string value)
    {
        Assert.Throws<DomainException>(() => ActivityTitle.FromDatabase(value));
    }

    [Fact]
    public void ActivityTitleFromDatabaseThrowsDomainExceptionWhenValueExceedsMaxLength()
    {
        Assert.Throws<DomainException>(() => ActivityTitle.FromDatabase(new string('a', ActivityTitle.MaxLength + 1)));
    }

    [Fact]
    public void ActivityTypeCreateNormalizesValidType()
    {
        ErrorOr<ActivityType> result = ActivityType.Create("  web.search-tool_1  ");

        Assert.False(result.IsError);
        Assert.Equal("web.search-tool_1", result.Value.Value);
        Assert.Equal("web.search-tool_1", result.Value.ToString());
    }

    [Fact]
    public void ActivityTypeCreateReturnsRequiredValidationWhenValueIsBlank()
    {
        AssertError(ActivityType.Create("   "), "ActivityType.Required");
    }

    [Fact]
    public void ActivityTypeCreateReturnsTooLongValidationWhenValueExceedsMaxLength()
    {
        AssertError(ActivityType.Create(new string('a', ActivityType.MaxLength + 1)), "ActivityType.TooLong");
    }

    [Theory]
    [InlineData("Uppercase")]
    [InlineData("has space")]
    [InlineData("emoji😀")]
    [InlineData("slash/type")]
    public void ActivityTypeCreateReturnsInvalidFormatValidationWhenValueIsNotWellFormed(string value)
    {
        AssertError(ActivityType.Create(value), "ActivityType.InvalidFormat");
    }

    [Theory]
    [InlineData("")]
    [InlineData("UpperCase")]
    [InlineData("has space")]
    [InlineData(" reasoning ")]
    public void ActivityTypeFromDatabaseThrowsDomainExceptionWhenValueIsInvalid(string value)
    {
        Assert.Throws<DomainException>(() => ActivityType.FromDatabase(value));
    }

    [Fact]
    public void ActivityDetailCreateTrimsWhitespaceWhenValueIsValidJson()
    {
        ErrorOr<ActivityDetail> result = ActivityDetail.Create("  {\"step\":1}  ");

        Assert.False(result.IsError);
        Assert.Equal("{\"step\":1}", result.Value.Value);
        Assert.Equal("{\"step\":1}", result.Value.ToString());
    }

    [Fact]
    public void ActivityDetailCreateReturnsRequiredValidationWhenValueIsBlank()
    {
        AssertError(ActivityDetail.Create("   "), "ActivityDetail.Required");
    }

    [Fact]
    public void ActivityDetailCreateReturnsTooLongValidationWhenValueExceedsMaxLength()
    {
        string oversizedJson = "\"" + new string('a', ActivityDetail.MaxLength) + "\"";

        AssertError(ActivityDetail.Create(oversizedJson), "ActivityDetail.TooLong");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"unterminated\":")]
    public void ActivityDetailCreateReturnsInvalidJsonValidationWhenValueIsNotJson(string value)
    {
        AssertError(ActivityDetail.Create(value), "ActivityDetail.InvalidJson");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" {\"step\":1} ")]
    [InlineData("not json")]
    [InlineData("{\"unterminated\":")]
    public void ActivityDetailFromDatabaseThrowsDomainExceptionWhenValueIsBlankUntrimmedOrNotJson(string value)
    {
        Assert.Throws<DomainException>(() => ActivityDetail.FromDatabase(value));
    }

    [Fact]
    public void ActivityDetailFromDatabaseReturnsValueForValidTrimmedJson()
    {
        ActivityDetail detail = ActivityDetail.FromDatabase("{\"step\":1}");

        Assert.Equal("{\"step\":1}", detail.Value);
    }

    [Fact]
    public void AgentTaskCreateTrimsWhitespaceWhenValueIsValid()
    {
        ErrorOr<AgentTask> result = AgentTask.Create("  Research the topic  ");

        Assert.False(result.IsError);
        Assert.Equal("Research the topic", result.Value.Value);
        Assert.Equal("Research the topic", result.Value.ToString());
    }

    [Fact]
    public void AgentTaskCreateReturnsRequiredValidationWhenValueIsBlank()
    {
        AssertError(AgentTask.Create("   "), "AgentTask.Required");
    }

    [Fact]
    public void AgentTaskCreateReturnsTooLongValidationWhenValueExceedsMaxLength()
    {
        AssertError(AgentTask.Create(new string('a', AgentTask.MaxLength + 1)), "AgentTask.TooLong");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" Research the topic ")]
    public void AgentTaskFromDatabaseThrowsDomainExceptionWhenValueIsBlankOrUntrimmed(string value)
    {
        Assert.Throws<DomainException>(() => AgentTask.FromDatabase(value));
    }

    [Fact]
    public void AgentTaskFromDatabaseThrowsDomainExceptionWhenValueExceedsMaxLength()
    {
        Assert.Throws<DomainException>(() => AgentTask.FromDatabase(new string('a', AgentTask.MaxLength + 1)));
    }

    private static void AssertError<T>(ErrorOr<T> result, string code)
    {
        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(code, error.Code);
    }
}