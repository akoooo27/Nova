using Chat.Domain;
using Chat.Domain.AgentRuns.ValueObjects;

using ErrorOr;

namespace Chat.Domain.Tests.AgentRuns.ValueObjects;

public sealed class ActivitySequenceTests
{
    [Fact]
    public void CreateReturnsValueWhenSequenceIsPositive()
    {
        ErrorOr<ActivitySequence> result = ActivitySequence.Create(7);

        Assert.False(result.IsError);
        Assert.Equal(7, result.Value.Value);
        Assert.Equal(7, result.Value.ToInt());
        Assert.Equal("7", result.Value.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreateReturnsNotPositiveValidationWhenSequenceIsNotPositive(int value)
    {
        ErrorOr<ActivitySequence> result = ActivitySequence.Create(value);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal("ActivitySequence.NotPositive", error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FromDatabaseThrowsDomainExceptionWhenSequenceIsNotPositive(int value)
    {
        Assert.Throws<DomainException>(() => ActivitySequence.FromDatabase(value));
    }

    [Fact]
    public void EqualityIsValueBasedThroughRecordSemantics()
    {
        ActivitySequence first = ActivitySequence.FromDatabase(3);
        ActivitySequence second = ActivitySequence.FromDatabase(3);

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.False(first != second);
    }

    [Fact]
    public void ComparisonOperatorsOrderBySequenceValue()
    {
        ActivitySequence low = ActivitySequence.FromDatabase(1);
        ActivitySequence high = ActivitySequence.FromDatabase(2);

        Assert.True(high > low);
        Assert.True(high >= low);
        Assert.True(low < high);
        Assert.True(low <= high);
        Assert.False(low > high);
        Assert.False(high < low);
    }

    [Fact]
    public void ComparisonOperatorsTreatEqualSequencesAsNeitherGreaterNorLess()
    {
        ActivitySequence first = ActivitySequence.FromDatabase(4);
        ActivitySequence second = ActivitySequence.FromDatabase(4);

        Assert.True(first >= second);
        Assert.True(first <= second);
        Assert.False(first > second);
        Assert.False(first < second);
    }

    [Fact]
    public void CompareToOrdersSmallerSequenceBeforeLarger()
    {
        ActivitySequence low = ActivitySequence.FromDatabase(1);
        ActivitySequence high = ActivitySequence.FromDatabase(9);

        Assert.True(low.CompareTo(high) < 0);
        Assert.True(high.CompareTo(low) > 0);
        Assert.Equal(0, low.CompareTo(ActivitySequence.FromDatabase(1)));
    }

    [Fact]
    public void CompareToThrowsArgumentNullExceptionForNullOther()
    {
        ActivitySequence sequence = ActivitySequence.FromDatabase(1);

        Assert.Throws<ArgumentNullException>(() => sequence.CompareTo(null));
    }

    [Fact]
    public void ImplementsIComparableSoStandardSortingProducesAscendingOrder()
    {
        List<ActivitySequence> sequences =
        [
            ActivitySequence.FromDatabase(3),
            ActivitySequence.FromDatabase(1),
            ActivitySequence.FromDatabase(2)
        ];

        sequences.Sort();

        Assert.Equal([1, 2, 3], sequences.Select(sequence => sequence.Value));
    }
}