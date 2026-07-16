using Chat.Domain.AgentRuns.ValueObjects;

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace Chat.Infrastructure.Agents.Research.Executors;

[YieldsOutput(typeof(string))]
internal sealed partial class WriterExecutor(AIAgent agent) : Executor("research-writer")
{
    [MessageHandler]
    private async ValueTask HandleAsync(ResearchState state, IWorkflowContext context, CancellationToken cancellationToken)
    {
        // No findings means every read failed. Don't ask the model to write a report from nothing
        // (it would fabricate); yield empty so the orchestrator's empty-response guard fails the
        // run cleanly rather than shipping a sourceless answer.
        if (state.Findings.Count == 0)
        {
            await context.YieldOutputAsync(string.Empty, cancellationToken);
            return;
        }

        await context.AddEventAsync(new ResearchProgressEvent
            (
                new ResearchProgress
                (
                    Sequence: state.NextSequence,
                    Kind: nameof(ActivityKind.Phase),
                    Type: ResearchActivityTypes.Phase,
                    Title: "Writing the report",
                    DetailJson: null
                )
            ),
            cancellationToken
        );

        string message = ResearchPrompts.Writer(state.Brief, state.Findings);

        AgentResponse response = await agent.RunAsync(message, cancellationToken: cancellationToken);

        await ResearchUsageEmitter.EmitAsync(context, response, cancellationToken);

        await context.YieldOutputAsync(response.Text, cancellationToken);
    }
}