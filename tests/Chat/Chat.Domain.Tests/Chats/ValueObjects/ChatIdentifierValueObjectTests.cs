using Chat.Domain;
using Chat.Domain.Chats.ValueObjects;

using ErrorOr;

namespace Chat.Domain.Tests.Chats.ValueObjects;

public sealed class ChatIdentifierValueObjectTests
{
    [Fact]
    public void ChatIdNewReturnsNonEmptyId()
    {
        ChatId id = ChatId.New();

        Assert.NotEqual(Guid.Empty, id.Value);
        Assert.Equal(id.Value.ToString(), id.ToString());
    }

    [Fact]
    public void ChatIdCreateReturnsValueWhenGuidIsNotEmpty()
    {
        Guid value = Guid.NewGuid();

        ErrorOr<ChatId> result = ChatId.Create(value);

        Assert.False(result.IsError);
        Assert.Equal(value, result.Value.Value);
        Assert.Equal(value.ToString(), result.Value.ToString());
    }

    [Fact]
    public void ChatIdCreateReturnsEmptyValidationWhenGuidIsEmpty()
    {
        ErrorOr<ChatId> result = ChatId.Create(Guid.Empty);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("ChatId.Empty", error.Code);
        Assert.Equal(ErrorType.Validation, error.Type);
    }

    [Fact]
    public void ChatIdFromDatabaseThrowsDomainExceptionWhenGuidIsEmpty()
    {
        Assert.Throws<DomainException>(() => ChatId.FromDatabase(Guid.Empty));
    }

    [Fact]
    public void ChatMessageIdNewReturnsNonEmptyId()
    {
        ChatMessageId id = ChatMessageId.New();

        Assert.NotEqual(Guid.Empty, id.Value);
        Assert.Equal(id.Value.ToString(), id.ToString());
    }

    [Fact]
    public void ChatMessageIdCreateReturnsValueWhenGuidIsNotEmpty()
    {
        Guid value = Guid.NewGuid();

        ErrorOr<ChatMessageId> result = ChatMessageId.Create(value);

        Assert.False(result.IsError);
        Assert.Equal(value, result.Value.Value);
        Assert.Equal(value.ToString(), result.Value.ToString());
    }

    [Fact]
    public void ChatMessageIdCreateReturnsEmptyValidationWhenGuidIsEmpty()
    {
        ErrorOr<ChatMessageId> result = ChatMessageId.Create(Guid.Empty);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("ChatMessageId.Empty", error.Code);
        Assert.Equal(ErrorType.Validation, error.Type);
    }

    [Fact]
    public void ChatMessageIdFromDatabaseThrowsDomainExceptionWhenGuidIsEmpty()
    {
        Assert.Throws<DomainException>(() => ChatMessageId.FromDatabase(Guid.Empty));
    }
}