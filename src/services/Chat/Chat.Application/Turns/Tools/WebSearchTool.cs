using System.ComponentModel;

using Chat.Application.Abstractions.Turns;
using Chat.Application.Abstractions.WebSearch;

namespace Chat.Application.Turns.Tools;

public sealed class WebSearchTool(IWebSearchClient client) : IAgentTool
{
    private const int DefaultResultCount = 5;

    private const string UnavailableMessage =
        "Web search is currently unavailable. Answer using your existing knowledge and note that you could not search the web.";

    public string Name => AgentToolNames.WebSearch;

    public Delegate CreateInvocation(TurnToolContext context) => SearchAsync;

    [Description("Search the public web for current information. Returns ranked results with snippets to cite.")]
    private async Task<WebSearchResponse> SearchAsync
    (
        [Description("The search query.")] string query,
        [Description("Maximum number of results to return (1-10).")] int count = DefaultResultCount,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            IReadOnlyList<WebSearchResult> results = await client.SearchAsync(query, count, cancellationToken);

            return new WebSearchResponse
            (
                Available: true,
                Results: results,
                Note: null
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031
        catch (Exception)
#pragma warning restore CA1031
        {
            return new WebSearchResponse
            (
                Available: false,
                Results: [],
                Note: UnavailableMessage
            );
        }
    }
}