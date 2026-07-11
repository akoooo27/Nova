using Chat.Domain.AgentRuns.ValueObjects;

using ErrorOr;

namespace Chat.Domain.AgentRuns;

public static class AgentRunErrors
{
    public static Error StaleActivitySequence(AgentRunId runId, ActivitySequence sequence) =>
        Error.Conflict
        (
            code: "AgentRun.StaleActivitySequence",
            description:
            $"Activity sequence '{sequence.Value}' is at or below the highest sequence already recorded for run '{runId.Value}'."
        );

    public static Error AlreadyFinished(AgentRunId runId) =>
        Error.Conflict
        (
            code: "AgentRun.AlreadyFinished",
            description: $"Agent run '{runId.Value}' is already finished."
        );

    public static Error FinishedBeforeStarted(AgentRunId runId) =>
        Error.Validation
        (
            code: "AgentRun.FinishedBeforeStarted",
            description: $"Agent run '{runId.Value}' cannot finish before it started."
        );
}