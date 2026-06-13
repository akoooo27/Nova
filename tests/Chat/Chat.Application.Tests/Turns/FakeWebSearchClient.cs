using Chat.Application.Abstractions.WebSearch;

namespace Chat.Application.Tests.Turns;

internal sealed class FakeWebSearchClient
(
    IReadOnlyList<WebSearchResult>? results = null,
    Exception? exception = null
) : IWebSearchClient
{
    private readonly IReadOnlyList<WebSearchResult> _results = results ?? [];

    public int Calls { get; private set; }

    public string? Query { get; private set; }

    public int? Count { get; private set; }

    public CancellationToken CancellationToken { get; private set; }

    public Task<IReadOnlyList<WebSearchResult>> SearchAsync
    (
        string query,
        int count,
        CancellationToken cancellationToken
    )
    {
        Calls++;
        Query = query;
        Count = count;
        CancellationToken = cancellationToken;

        return exception is null
            ? Task.FromResult(_results)
            : Task.FromException<IReadOnlyList<WebSearchResult>>(exception);
    }
}