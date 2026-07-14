using System.Text.Json;

using Chat.Application.Abstractions.WebSearch;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Infrastructure.Options;

using Microsoft.Agents.AI.Workflows;

namespace Chat.Infrastructure.Agents.Research.Executors;

internal sealed partial class SearchExecutor(IWebSearchClient searchClient, ResearchOptions options) : Executor("research-search")
{
    private const int ResultsPerQuery = 5;
    private const int QueriesPerRound = 3;

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
                    Title: "Searching the web",
                    DetailJson: null
                )
            ),
            cancellationToken
        );

        List<string> candidates = [.. state.CandidateUrls];
        int searchesUsed = state.SearchesUsed;

        foreach (string question in state.OpenQuestions.Take(QueriesPerRound))
        {
            if (searchesUsed >= options.MaxSearches)
            {
                break;
            }

            await context.AddEventAsync(new ResearchProgressEvent
                (
                    new ResearchProgress
                    (
                        Sequence: sequence++,
                        Kind: nameof(ActivityKind.ToolCall),
                        Type: ResearchActivityTypes.Search,
                        Title: $"Searching for: {question}",
                        DetailJson: JsonSerializer.Serialize(new { query = question })
                    )
                ),
                cancellationToken
            );

            searchesUsed++;

            IReadOnlyList<WebSearchResult> results = await searchClient.SearchAsync
            (
                query: question,
                count: ResultsPerQuery,
                cancellationToken: cancellationToken
            );

#pragma warning disable S3267
            foreach (WebSearchResult result in results)
#pragma warning restore S3267
            {
                string url = result.ReferencedSite;

                if (!candidates.Contains(url) && !state.AttemptedUrls.Contains(url) &&
                    Uri.IsWellFormedUriString(url, UriKind.Absolute))
                {
                    candidates.Add(url);
                }
            }
        }

        await context.SendMessageAsync(state with
        {
            CandidateUrls = candidates,
            SearchesUsed = searchesUsed,
            NextSequence = sequence
        },
        cancellationToken: cancellationToken);
    }
}