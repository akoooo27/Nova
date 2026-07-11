using Chat.Domain;
using Chat.Domain.AgentRuns.ValueObjects;

using ErrorOr;

namespace Chat.Domain.Tests.AgentRuns.ValueObjects;

public sealed class AgentRunIdentifierValueObjectTests
{
    [Fact]
    public void AgentRunIdNewCreatesUniqueVersionSevenIds()
    {
        AgentRunId first = AgentRunId.New();
        AgentRunId second = AgentRunId.New();

        Assert.NotEqual(Guid.Empty, first.Value);
        Assert.Equal(7, first.Value.Version);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void AgentRunIdCreateReturnsValueForNonEmptyGuid()
    {
        Guid value = Guid.CreateVersion7();

        ErrorOr<AgentRunId> result = AgentRunId.Create(value);

        Assert.False(result.IsError);
        Assert.Equal(value, result.Value.Value);
        Assert.Equal(value.ToString(), result.Value.ToString());
    }

    [Fact]
    public void AgentRunIdCreateReturnsEmptyValidationForEmptyGuid()
    {
        ErrorOr<AgentRunId> result = AgentRunId.Create(Guid.Empty);

        Assert.True(result.IsError);
        Assert.Equal("AgentRunId.Empty", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void AgentRunIdFromDatabaseThrowsDomainExceptionForEmptyGuid()
    {
        Assert.Throws<DomainException>(() => AgentRunId.FromDatabase(Guid.Empty));
    }

    [Fact]
    public void AgentRunActivityIdNewCreatesUniqueVersionSevenIds()
    {
        AgentRunActivityId first = AgentRunActivityId.New();
        AgentRunActivityId second = AgentRunActivityId.New();

        Assert.NotEqual(Guid.Empty, first.Value);
        Assert.Equal(7, first.Value.Version);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void AgentRunActivityIdCreateReturnsValueForNonEmptyGuid()
    {
        Guid value = Guid.CreateVersion7();

        ErrorOr<AgentRunActivityId> result = AgentRunActivityId.Create(value);

        Assert.False(result.IsError);
        Assert.Equal(value, result.Value.Value);
    }

    [Fact]
    public void AgentRunActivityIdCreateReturnsEmptyValidationForEmptyGuid()
    {
        ErrorOr<AgentRunActivityId> result = AgentRunActivityId.Create(Guid.Empty);

        Assert.True(result.IsError);
        Assert.Equal("AgentRunActivityId.Empty", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void AgentRunActivityIdFromDatabaseThrowsDomainExceptionForEmptyGuid()
    {
        Assert.Throws<DomainException>(() => AgentRunActivityId.FromDatabase(Guid.Empty));
    }
}