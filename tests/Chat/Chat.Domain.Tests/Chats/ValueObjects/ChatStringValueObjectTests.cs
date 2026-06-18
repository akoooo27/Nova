using Chat.Domain;
using Chat.Domain.Chats.ValueObjects;

using ErrorOr;

namespace Chat.Domain.Tests.Chats.ValueObjects;

public sealed class ChatStringValueObjectTests
{
    [Fact]
    public void ChatTitleCreateTrimsWhitespaceWhenValueIsValid()
    {
        ErrorOr<ChatTitle> result = ChatTitle.Create("  Planning chat  ");

        Assert.False(result.IsError);
        Assert.Equal("Planning chat", result.Value.Value);
        Assert.Equal("Planning chat", result.Value.ToString());
    }

    [Fact]
    public void ChatTitleCreateReturnsRequiredValidationWhenValueIsBlank()
    {
        ErrorOr<ChatTitle> result = ChatTitle.Create("   ");

        AssertError(result, ErrorType.Validation, "ChatTitle.Required");
    }

    [Fact]
    public void ChatTitleCreateReturnsTooLongValidationWhenValueExceedsMaxLength()
    {
        ErrorOr<ChatTitle> result = ChatTitle.Create(new string('a', ChatTitle.MaxLength + 1));

        AssertError(result, ErrorType.Validation, "ChatTitle.TooLong");
    }

    [Fact]
    public void ChatTitleFromDatabaseThrowsDomainExceptionWhenValueIsBlank()
    {
        Assert.Throws<DomainException>(() => ChatTitle.FromDatabase(""));
    }

    [Fact]
    public void ChatTitleFromDatabaseThrowsDomainExceptionWhenValueExceedsMaxLength()
    {
        Assert.Throws<DomainException>(() => ChatTitle.FromDatabase(new string('a', ChatTitle.MaxLength + 1)));
    }

    [Fact]
    public void MessageContentCreateTrimsWhitespaceWhenValueIsValid()
    {
        ErrorOr<MessageContent> result = MessageContent.Create("  Hello  ");

        Assert.False(result.IsError);
        Assert.Equal("Hello", result.Value.Value);
        Assert.Equal("Hello", result.Value.ToString());
    }

    [Fact]
    public void MessageContentCreateReturnsRequiredValidationWhenValueIsBlank()
    {
        ErrorOr<MessageContent> result = MessageContent.Create("   ");

        AssertError(result, ErrorType.Validation, "MessageContent.Required");
    }

    [Fact]
    public void MessageContentCreateReturnsTooLongValidationWhenValueExceedsMaxLength()
    {
        ErrorOr<MessageContent> result = MessageContent.Create(new string('a', MessageContent.MaxLength + 1));

        AssertError(result, ErrorType.Validation, "MessageContent.TooLong");
    }

    [Fact]
    public void MessageContentFromDatabaseThrowsDomainExceptionWhenValueIsBlank()
    {
        Assert.Throws<DomainException>(() => MessageContent.FromDatabase(""));
    }

    [Fact]
    public void MessageContentFromDatabaseThrowsDomainExceptionWhenValueExceedsMaxLength()
    {
        Assert.Throws<DomainException>(() => MessageContent.FromDatabase(new string('a', MessageContent.MaxLength + 1)));
    }

    [Fact]
    public void FailureReasonCreateTrimsWhitespaceWhenValueIsValid()
    {
        ErrorOr<FailureReason> result = FailureReason.Create("  Provider failed  ");

        Assert.False(result.IsError);
        Assert.Equal("Provider failed", result.Value.Value);
        Assert.Equal("Provider failed", result.Value.ToString());
    }

    [Fact]
    public void FailureReasonCreateReturnsRequiredValidationWhenValueIsBlank()
    {
        ErrorOr<FailureReason> result = FailureReason.Create("   ");

        AssertError(result, ErrorType.Validation, "FailureReason.Required");
    }

    [Fact]
    public void FailureReasonCreateReturnsTooLongValidationWhenValueExceedsMaxLength()
    {
        ErrorOr<FailureReason> result = FailureReason.Create(new string('a', FailureReason.MaxLength + 1));

        AssertError(result, ErrorType.Validation, "FailureReason.TooLong");
    }

    [Fact]
    public void FailureReasonFromDatabaseThrowsDomainExceptionWhenValueIsBlank()
    {
        Assert.Throws<DomainException>(() => FailureReason.FromDatabase(""));
    }

    [Fact]
    public void FailureReasonFromDatabaseThrowsDomainExceptionWhenValueExceedsMaxLength()
    {
        Assert.Throws<DomainException>(() => FailureReason.FromDatabase(new string('a', FailureReason.MaxLength + 1)));
    }

    [Fact]
    public void SiblingIndexFirstReturnsZero()
    {
        SiblingIndex index = SiblingIndex.First();

        Assert.Equal(0, index.Value);
    }

    [Fact]
    public void SiblingIndexNextReturnsExistingCount()
    {
        SiblingIndex index = SiblingIndex.Next(2);

        Assert.Equal(2, index.Value);
    }

    [Fact]
    public void SiblingIndexNextThrowsDomainExceptionWhenExistingCountIsNegative()
    {
        Assert.Throws<DomainException>(() => SiblingIndex.Next(-1));
    }

    [Fact]
    public void SiblingIndexFromDatabaseThrowsDomainExceptionWhenValueIsNegative()
    {
        Assert.Throws<DomainException>(() => SiblingIndex.FromDatabase(-1));
    }

    private static void AssertError<T>(ErrorOr<T> result, ErrorType type, string code)
    {
        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(type, error.Type);
        Assert.Equal(code, error.Code);
    }
}