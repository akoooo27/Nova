using Chat.Domain;
using Chat.Domain.SharedChats.ValueObjects;

using ErrorOr;

namespace Chat.Domain.Tests.SharedChats;

public sealed class SharedChatIdTests
{
    [Fact]
    public void NewReturnsNonEmptyRandomVersionFourId()
    {
        SharedChatId id = SharedChatId.New();

        Assert.NotEqual(Guid.Empty, id.Value);
        Assert.Equal(4, id.Value.Version);
        Assert.Equal(id.Value.ToString(), id.ToString());
    }

    [Fact]
    public void CreateReturnsValueWhenGuidIsNotEmpty()
    {
        Guid value = Guid.NewGuid();

        ErrorOr<SharedChatId> result = SharedChatId.Create(value);

        Assert.False(result.IsError);
        Assert.Equal(value, result.Value.Value);
    }

    [Fact]
    public void CreateReturnsEmptyValidationWhenGuidIsEmpty()
    {
        ErrorOr<SharedChatId> result = SharedChatId.Create(Guid.Empty);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("SharedChatId.Empty", error.Code);
        Assert.Equal(ErrorType.Validation, error.Type);
    }

    [Fact]
    public void FromDatabaseThrowsDomainExceptionWhenGuidIsEmpty()
    {
        Assert.Throws<DomainException>(() => SharedChatId.FromDatabase(Guid.Empty));
    }
}