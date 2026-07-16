using System.Text.Json;

using Chat.Domain.AgentRuns.ValueObjects;

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace Chat.Infrastructure.Agents.Research.Executors;

[SendsMessage(typeof(ResearchState))]
internal sealed partial class PlannerExecutor(AIAgent agent) : Executor("research-planner")
{
    [MessageHandler]
    private async ValueTask HandleAsync(ResearchState state, IWorkflowContext context, CancellationToken cancellationToken)
    {
        int sequence = state.NextSequence;

        await context.AddEventAsync(new ResearchProgressEvent
            (
                new ResearchProgress
                (
                    Sequence: sequence++,
                    Kind: nameof(ActivityKind.Phase),
                    Type: ResearchActivityTypes.Phase,
                    Title: "Planning",
                    DetailJson: null
                )
            ),
            cancellationToken
        );

        string message = ResearchPrompts.Planner(state.Brief, state.History);

        AgentResponse response = await agent.RunAsync(message, cancellationToken: cancellationToken);

        await ResearchUsageEmitter.EmitAsync(context, response, cancellationToken);

        IReadOnlyList<string> questions = ResearchPrompts.ParseQueries(response.Text);

        if (questions.Count == 0)
        {
            questions = [state.Brief];
        }

        await context.AddEventAsync(new ResearchProgressEvent
            (
                new ResearchProgress
                (
                    Sequence: sequence++,
                    Kind: nameof(ActivityKind.Thought),
                    Type: ResearchActivityTypes.Reasoning,
                    Title: $"Planned {questions.Count} research questions",
                    DetailJson: JsonSerializer.Serialize(new { questions })
                )
            ),
            cancellationToken
        );

        await context.SendMessageAsync(state with
        {
            OpenQuestions = questions,
            NextSequence = sequence
        },
        cancellationToken: cancellationToken);
    }
}