using Chat.Domain.AgentRuns;
using Chat.Domain.AgentRuns.Entities;
using Chat.Domain.AgentRuns.ValueObjects;

using ErrorOr;

namespace Chat.Domain.Tests.AgentRuns;

public sealed class AgentRunTests
{
    [Fact]
    public void StartInitializesRunWithProvidedMetadataZeroUsageAndNoActivities()
    {
        AgentRun run = TestAgentRunFactory.StartRun();

        Assert.NotEqual(Guid.Empty, run.Id.Value);
        Assert.Equal(AgentRunKind.Research, run.Kind);
        Assert.Equal("Research the topic", run.Task.Value);
        Assert.Equal(TestAgentRunFactory.StartedAt, run.StartedAt);
        Assert.Equal(TokenUsage.Zero, run.Usage);
        Assert.Null(run.FinishedAt);
        Assert.Empty(run.Activities);
        Assert.Null(run.CurrentPhase);
    }

    [Fact]
    public void AppendActivityAddsActivityWithProvidedValuesAndReturnsIt()
    {
        AgentRun run = TestAgentRunFactory.StartRun();
        DateTimeOffset occurredAt = TestAgentRunFactory.StartedAt.AddSeconds(5);

        ErrorOr<AgentRunActivity> result = run.AppendActivity
        (
            sequence: TestAgentRunFactory.Sequence(1),
            kind: ActivityKind.ToolCall,
            type: TestAgentRunFactory.Type("web.search"),
            title: TestAgentRunFactory.Title("Searching"),
            detail: TestAgentRunFactory.Detail("{\"query\":\"x\"}"),
            occurredAt: occurredAt
        );

        Assert.False(result.IsError);
        AgentRunActivity activity = result.Value;
        Assert.Same(activity, Assert.Single(run.Activities));
        Assert.Equal(run.Id, activity.RunId);
        Assert.Equal(1, activity.Sequence.Value);
        Assert.Equal(ActivityKind.ToolCall, activity.Kind);
        Assert.Equal("web.search", activity.Type.Value);
        Assert.Equal("Searching", activity.Title.Value);
        Assert.Equal("{\"query\":\"x\"}", activity.Detail!.Value);
        Assert.Equal(occurredAt, activity.OccurredAt);
    }

    [Fact]
    public void AppendActivityAllowsNullDetail()
    {
        AgentRun run = TestAgentRunFactory.StartRun();

        ErrorOr<AgentRunActivity> result = run.AppendActivity
        (
            sequence: TestAgentRunFactory.Sequence(1),
            kind: ActivityKind.Thought,
            type: TestAgentRunFactory.Type(),
            title: TestAgentRunFactory.Title(),
            detail: null,
            occurredAt: TestAgentRunFactory.StartedAt.AddSeconds(1)
        );

        Assert.False(result.IsError);
        Assert.Null(result.Value.Detail);
    }

    [Fact]
    public void AppendActivityAcceptsStrictlyIncreasingSequences()
    {
        AgentRun run = TestAgentRunFactory.StartRun();

        Assert.False(TestAgentRunFactory.AppendActivity(run, 1).IsError);
        Assert.False(TestAgentRunFactory.AppendActivity(run, 2).IsError);
        Assert.False(TestAgentRunFactory.AppendActivity(run, 10).IsError);

        Assert.Equal(3, run.Activities.Count);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(3)]
    public void AppendActivityRejectsSequenceAtOrBelowHighestRecorded(int staleSequence)
    {
        AgentRun run = TestAgentRunFactory.StartRun();
        Assert.False(TestAgentRunFactory.AppendActivity(run, 5).IsError);

        ErrorOr<AgentRunActivity> result = TestAgentRunFactory.AppendActivity(run, staleSequence);

        AssertError(result, ErrorType.Conflict, "AgentRun.StaleActivitySequence");
        Assert.Single(run.Activities);
    }

    [Fact]
    public void AppendActivityReturnsAlreadyFinishedWhenRunHasFinished()
    {
        AgentRun run = TestAgentRunFactory.StartRun();
        Assert.False(run.Finish(TestAgentRunFactory.StartedAt.AddMinutes(1)).IsError);

        ErrorOr<AgentRunActivity> result = TestAgentRunFactory.AppendActivity(run, 1);

        AssertError(result, ErrorType.Conflict, "AgentRun.AlreadyFinished");
        Assert.Empty(run.Activities);
    }

    [Fact]
    public void CurrentPhaseReflectsHighestSequencedPhaseActivity()
    {
        AgentRun run = TestAgentRunFactory.StartRun();
        Assert.False(TestAgentRunFactory.AppendActivity(run, 1, ActivityKind.Phase, "Planning").IsError);
        Assert.False(TestAgentRunFactory.AppendActivity(run, 2, ActivityKind.Thought, "Ignored").IsError);
        Assert.False(TestAgentRunFactory.AppendActivity(run, 3, ActivityKind.Phase, "Executing").IsError);

        Assert.Equal("Executing", run.CurrentPhase!.Value);
    }

    [Fact]
    public void CurrentPhaseIgnoresNonPhaseActivities()
    {
        AgentRun run = TestAgentRunFactory.StartRun();
        Assert.False(TestAgentRunFactory.AppendActivity(run, 1, ActivityKind.Thought, "Thinking").IsError);
        Assert.False(TestAgentRunFactory.AppendActivity(run, 2, ActivityKind.ToolCall, "Calling").IsError);

        Assert.Null(run.CurrentPhase);
    }

    [Fact]
    public void RecordUsageAccumulatesAcrossCalls()
    {
        AgentRun run = TestAgentRunFactory.StartRun();

        Assert.False(run.RecordUsage(TestAgentRunFactory.Usage(10, 20)).IsError);
        Assert.False(run.RecordUsage(TestAgentRunFactory.Usage(5, 7)).IsError);

        Assert.Equal(15, run.Usage.InputTokens);
        Assert.Equal(27, run.Usage.OutputTokens);
    }

    [Fact]
    public void RecordUsageReturnsAlreadyFinishedWhenRunHasFinishedAndLeavesUsageUnchanged()
    {
        AgentRun run = TestAgentRunFactory.StartRun();
        Assert.False(run.RecordUsage(TestAgentRunFactory.Usage(10, 20)).IsError);
        Assert.False(run.Finish(TestAgentRunFactory.StartedAt.AddMinutes(1)).IsError);

        ErrorOr<Success> result = run.RecordUsage(TestAgentRunFactory.Usage(5, 5));

        AssertError(result, ErrorType.Conflict, "AgentRun.AlreadyFinished");
        Assert.Equal(10, run.Usage.InputTokens);
        Assert.Equal(20, run.Usage.OutputTokens);
    }

    [Fact]
    public void FinishSetsFinishedAtWhenAfterStart()
    {
        AgentRun run = TestAgentRunFactory.StartRun();
        DateTimeOffset finishedAt = TestAgentRunFactory.StartedAt.AddMinutes(3);

        ErrorOr<Success> result = run.Finish(finishedAt);

        Assert.False(result.IsError);
        Assert.Equal(finishedAt, run.FinishedAt);
    }

    [Fact]
    public void FinishAllowsFinishingAtTheStartInstant()
    {
        AgentRun run = TestAgentRunFactory.StartRun();

        ErrorOr<Success> result = run.Finish(TestAgentRunFactory.StartedAt);

        Assert.False(result.IsError);
        Assert.Equal(TestAgentRunFactory.StartedAt, run.FinishedAt);
    }

    [Fact]
    public void FinishReturnsFinishedBeforeStartedWhenTimestampPrecedesStartAndLeavesRunOpen()
    {
        AgentRun run = TestAgentRunFactory.StartRun();

        ErrorOr<Success> result = run.Finish(TestAgentRunFactory.StartedAt.AddSeconds(-1));

        AssertError(result, ErrorType.Validation, "AgentRun.FinishedBeforeStarted");
        Assert.Null(run.FinishedAt);
    }

    [Fact]
    public void FinishReturnsAlreadyFinishedOnSecondCallAndKeepsOriginalTimestamp()
    {
        AgentRun run = TestAgentRunFactory.StartRun();
        DateTimeOffset finishedAt = TestAgentRunFactory.StartedAt.AddMinutes(1);
        Assert.False(run.Finish(finishedAt).IsError);

        ErrorOr<Success> result = run.Finish(TestAgentRunFactory.StartedAt.AddMinutes(2));

        AssertError(result, ErrorType.Conflict, "AgentRun.AlreadyFinished");
        Assert.Equal(finishedAt, run.FinishedAt);
    }

    private static void AssertError<T>(ErrorOr<T> result, ErrorType type, string code)
    {
        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(type, error.Type);
        Assert.Equal(code, error.Code);
    }
}