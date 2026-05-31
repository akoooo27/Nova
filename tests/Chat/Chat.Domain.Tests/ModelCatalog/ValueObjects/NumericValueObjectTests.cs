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

    [Fact]
    public void SortOrderFirstReturnsOne()
    {
        SortOrder sortOrder = SortOrder.First;

        Assert.Equal(1, sortOrder.Value);
        Assert.Equal("1", sortOrder.ToString());
    }

    [Fact]
    public void SortOrderCreateReturnsValueWhenValueIsPositive()
    {
        ErrorOr<SortOrder> result = SortOrder.Create(10);

        Assert.False(result.IsError);
        Assert.Equal(10, result.Value.Value);
        Assert.Equal("10", result.Value.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SortOrderCreateReturnsInvalidValidationWhenValueIsLessThanOne(int value)
    {
        ErrorOr<SortOrder> result = SortOrder.Create(value);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("SortOrder.Invalid", error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SortOrderFromDatabaseThrowsDomainExceptionWhenValueIsLessThanOne(int value)
    {
        Assert.Throws<DomainException>(() => SortOrder.FromDatabase(value));
    }
}