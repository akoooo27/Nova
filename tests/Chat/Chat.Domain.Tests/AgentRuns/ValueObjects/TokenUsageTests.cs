using Chat.Domain;
using Chat.Domain.AgentRuns.ValueObjects;

using ErrorOr;

namespace Chat.Domain.Tests.AgentRuns.ValueObjects;

public sealed class TokenUsageTests
{
    [Fact]
    public void ZeroHasNoInputOrOutputTokens()
    {
        TokenUsage usage = TokenUsage.Zero;

        Assert.Equal(0, usage.InputTokens);
        Assert.Equal(0, usage.OutputTokens);
    }

    [Fact]
    public void CreateReturnsUsageWhenTokenCountsAreNonNegative()
    {
        ErrorOr<TokenUsage> result = TokenUsage.Create(inputTokens: 12, outputTokens: 34);

        Assert.False(result.IsError);
        Assert.Equal(12, result.Value.InputTokens);
        Assert.Equal(34, result.Value.OutputTokens);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(-1, -1)]
    public void CreateReturnsNegativeValidationWhenEitherCountIsNegative(int inputTokens, int outputTokens)
    {
        ErrorOr<TokenUsage> result = TokenUsage.Create(inputTokens, outputTokens);

        Assert.True(result.IsError);
        Assert.Equal("TokenUsage.Negative", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void AddSumsInputAndOutputTokensWithoutMutatingOperands()
    {
        TokenUsage first = TokenUsage.FromDatabase(10, 20);
        TokenUsage second = TokenUsage.FromDatabase(3, 4);

        TokenUsage total = first.Add(second);

        Assert.Equal(13, total.InputTokens);
        Assert.Equal(24, total.OutputTokens);
        Assert.Equal(10, first.InputTokens);
        Assert.Equal(20, first.OutputTokens);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void FromDatabaseThrowsDomainExceptionWhenEitherCountIsNegative(int inputTokens, int outputTokens)
    {
        Assert.Throws<DomainException>(() => TokenUsage.FromDatabase(inputTokens, outputTokens));
    }
}