using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Domain.Tests.Shared;

public sealed class AssetKeyTests
{
    [Fact]
    public void CreateTrimsWhitespaceWhenValueIsPresent()
    {
        ErrorOr<AssetKey> result = AssetKey.Create("  llm-providers/anthropic.svg  ");

        Assert.False(result.IsError);
        Assert.Equal("llm-providers/anthropic.svg", result.Value.Value);
        Assert.Equal("llm-providers/anthropic.svg", result.Value.ToString());
    }

    [Fact]
    public void CreateReturnsRequiredValidationWhenValueIsBlank()
    {
        ErrorOr<AssetKey> result = AssetKey.Create("   ");

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("AssetKey.Required", error.Code);
    }

    [Theory]
    [InlineData("https://cdn.example.com/logo.svg")]
    [InlineData("/llm-providers/anthropic.svg")]
    [InlineData("llm providers/anthropic.svg")]
    [InlineData("llm-providers/../anthropic.svg")]
    public void CreateTreatsAssetKeyAsOpaqueExternalDataWhenValueIsPresent(string value)
    {
        ErrorOr<AssetKey> result = AssetKey.Create(value);

        Assert.False(result.IsError);
        Assert.Equal(value, result.Value.Value);
    }
}