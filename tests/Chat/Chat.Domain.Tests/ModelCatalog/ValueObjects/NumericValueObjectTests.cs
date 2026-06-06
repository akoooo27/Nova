using Chat.Domain;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

namespace Chat.Domain.Tests.ModelCatalog.ValueObjects;

public sealed class NumericValueObjectTests
{
    [Fact]
    public void ContextWindowCreateReturnsValueWhenValueIsPositive()
    {
        ErrorOr<ContextWindow> result = ContextWindow.Create(128000);

        Assert.False(result.IsError);
        Assert.Equal(128000, result.Value.Value);
        Assert.Equal("128000", result.Value.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ContextWindowCreateReturnsInvalidValidationWhenValueIsLessThanOne(int value)
    {
        ErrorOr<ContextWindow> result = ContextWindow.Create(value);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("ContextWindow.Invalid", error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ContextWindowFromDatabaseThrowsDomainExceptionWhenValueIsLessThanOne(int value)
    {
        Assert.Throws<DomainException>(() => ContextWindow.FromDatabase(value));
    }
}