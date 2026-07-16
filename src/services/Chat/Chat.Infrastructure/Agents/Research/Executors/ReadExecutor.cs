using System.Text.Json;

using Chat.Application.Abstractions.WebRead;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Infrastructure.Options;

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace Chat.Infrastructure.Agents.Research.Executors;

[SendsMessage(typeof(ResearchState))]
internal sealed partial class ReadExecutor(IUrlReader urlReader, AIAgent condenser, ResearchOptions options)
    : Executor("research-read")
{
    private const int ReadsPerRound = 3;

    [MessageHandler]
    private async ValueTask HandleAsync(ResearchState state, IWorkflowContext context, CancellationToken cancellationToken)
    {
        int sequence = state.NextSequence;
        List<ResearchFinding> findings = [.. state.Findings];
        List<string> remaining = [.. state.CandidateUrls];
        List<string> attempted = [.. state.AttemptedUrls];
        int sourcesRead = state.SourcesRead;
        int readsThisRound = 0;
        int readFailures = 0;
        Exception? lastReadError = null;

        while (remaining.Count > 0 && readsThisRound < ReadsPerRound && sourcesRead < options.MaxSourcesToRead)
        {
            // Drain newest-first: later rounds' critic-driven URLs are more targeted than
            // stale leftovers, so read them before the backlog rather than letting them starve.
            int last = remaining.Count - 1;
            string url = remaining[last];
            remaining.RemoveAt(last);
            readsThisRound++;
            attempted.Add(url);

            string host = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
                ? uri.Host
                : url;

            await context.AddEventAsync(new ResearchProgressEvent
                (
                    new ResearchProgress
                    (
                        Sequence: sequence++,
                        Kind: nameof(ActivityKind.ToolCall),
                        Type: ResearchActivityTypes.Read,
                        Title: $"Reading {host}",
                        DetailJson: JsonSerializer.Serialize(new { url })
                    )
                ),
                cancellationToken
            );

            if (uri is null)
            {
                continue;
            }

            ReadPage page;

            try
            {
                page = await urlReader.ReadAsync(uri, cancellationToken);
            }
#pragma warning disable CA1031 // A single unreadable page must not kill the run.
            catch (Exception readError)
#pragma warning restore CA1031
            {
                readFailures++;
                lastReadError = readError;

                await context.AddEventAsync(new ResearchProgressEvent
                    (
                        new ResearchProgress
                        (
                            Sequence: sequence++,
                            Kind: nameof(ActivityKind.Error),
                            Type: ResearchActivityTypes.ReadFailed,
                            Title: $"Could not read {host}; skipping",
                            DetailJson: JsonSerializer.Serialize(new { url })
                        )
                    ),
                    cancellationToken
                );
                continue;
            }

            string condensed = ResearchPrompts.Condense
            (
                brief: state.Brief,
                url: url,
                title: page.Title,
                markdown: page.Markdown
            );

            AgentResponse response = await condenser.RunAsync(condensed, cancellationToken: cancellationToken);

            await ResearchUsageEmitter.EmitAsync(context, response, cancellationToken);

            if (string.IsNullOrWhiteSpace(response.Text))
            {
                continue;
            }

            sourcesRead++;

            string title = page.Title ?? host;

            ResearchFinding finding = new
            (
                Url: url,
                Title: title,
                Notes: response.Text.Trim()
            );
            findings.Add(finding);

            await context.AddEventAsync(new ResearchProgressEvent
                (
                    new ResearchProgress
                    (
                        Sequence: sequence++,
                        Kind: nameof(ActivityKind.Observation),
                        Type: ResearchActivityTypes.Source,
                        Title: title,
                        DetailJson: JsonSerializer.Serialize(new { url, title, domain = host })
                    )
                ),
                cancellationToken
            );
        }

        // Isolated unreadable pages are skipped above. But if this step attempted
        // reads and still gathered nothing, the failure is systemic (bad config,
        // auth, or connectivity) rather than a single bad page — fail loudly with
        // the real cause so the run surfaces an error instead of an empty report.
        if (sourcesRead == 0 && readFailures > 0)
        {
            throw new WorkflowExecutionException
            (
                $"All {readFailures} URL read(s) failed; aborting research. Last error: {lastReadError?.Message}",
                lastReadError
            );
        }

        await context.SendMessageAsync(state with
        {
            CandidateUrls = remaining,
            Findings = findings,
            AttemptedUrls = attempted,
            SourcesRead = sourcesRead,
            NextSequence = sequence
        },
        cancellationToken: cancellationToken);
    }
}