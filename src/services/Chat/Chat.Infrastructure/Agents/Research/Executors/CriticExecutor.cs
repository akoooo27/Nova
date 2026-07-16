using System.Text.Json;

using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Infrastructure.Options;

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace Chat.Infrastructure.Agents.Research.Executors;

/// <summary>
/// Decides one more round or done. "Done" = OpenQuestions comes out empty — the workflow
/// edges route on that (empty → Writer, non-empty → Search).
/// </summary>
[SendsMessage(typeof(ResearchState))]
internal sealed partial class CriticExecutor(AIAgent agent, ResearchOptions options) : Executor("research-critic")
{
    [MessageHandler]
    private async ValueTask HandleAsync(ResearchState state, IWorkflowContext context, CancellationToken cancellationToken)
    {
        int sequence = state.NextSequence;
        int round = state.Round + 1;

        await context.AddEventAsync(new ResearchProgressEvent
            (
                new ResearchProgress
                (
                    Sequence: sequence++,
                    Kind: nameof(ActivityKind.Phase),
                    Type: ResearchActivityTypes.Phase,
                    Title: "Analyzing findings",
                    DetailJson: null
                )
            ),
            cancellationToken
        );

        IReadOnlyList<string> nextQuestions = [];

        bool hardStop = round >= options.MaxRounds
                        || state.SearchesUsed >= options.MaxSearches
                        || state.SourcesRead >= options.MaxSourcesToRead;

        if (!hardStop)
        {
            if (state.Findings.Count == 0)
            {
                // Nothing gathered yet (every read failed or condensed to nothing). Retry the
                // outstanding questions for another search/read pass instead of writing an empty
                // report; the round/search/source budgets above still bound the retries.
                nextQuestions = state.OpenQuestions.Count > 0 ? state.OpenQuestions : [state.Brief];
            }
            else
            {
                string message = ResearchPrompts.Critic(state.Brief, state.Findings);

                AgentResponse response = await agent.RunAsync(message, cancellationToken: cancellationToken);

                await ResearchUsageEmitter.EmitAsync(context, response, cancellationToken);

                if (!response.Text.Trim().StartsWith("DONE", StringComparison.OrdinalIgnoreCase))
                {
                    nextQuestions = ResearchPrompts.ParseQueries(response.Text);
                }
            }
        }

        string reasoning;

        if (nextQuestions.Count > 0)
        {
            reasoning = $"Identified {nextQuestions.Count} gaps; searching again";
        }
        else if (state.Findings.Count == 0)
        {
            reasoning = "No sources could be gathered; ending research";
        }
        else
        {
            reasoning = "Coverage sufficient; moving to the report";
        }

        await context.AddEventAsync(new ResearchProgressEvent
            (
                new ResearchProgress
                (
                    Sequence: sequence++,
                    Kind: nameof(ActivityKind.Thought),
                    Type: ResearchActivityTypes.Reasoning,
                    Title: reasoning,
                    DetailJson: JsonSerializer.Serialize(new { round, gaps = nextQuestions })
                )
            ),
            cancellationToken
        );

        await context.SendMessageAsync(state with
        {
            OpenQuestions = nextQuestions,
            Round = round,
            NextSequence = sequence
        },
        cancellationToken: cancellationToken);
    }
}