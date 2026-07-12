using Chat.Application.Turns;

namespace Chat.Application.Abstractions.AgentRuns;

/// <summary>
/// Executes one agent run and streams its progress as TurnEvents: AgentActivityEvents and
/// UsageEvents during the run, then the finished report as a single final TokenEvent
/// (spec decision 4). Implementations live in the infrastructure quarantine and are
/// resolved per <c>AgentRunKind</c> via keyed DI.
/// </summary>
public interface IAgentRunRunner
{
    IAsyncEnumerable<TurnEvent> RunAsync
    (
        AgentRunContext context,
        IWorkflowCheckpointStore checkpointStore,
        CancellationToken cancellationToken
    );
}