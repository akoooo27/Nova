namespace Chat.Application.Abstractions.WebSearch;

public interface IWebSearchClient
{
    Task<IReadOnlyList<WebSearchResult>> SearchAsync
    (
        string query,
        int count,
        CancellationToken cancellationToken
    );
}

public sealed record WebSearchResult
(
    string Title,
    string ReferencedSite,
    string Snippet,
    string? PublishedAt
);

public sealed record WebSearchResponse
(
    bool Available,
    IReadOnlyList<WebSearchResult> Results,
    string? Note
);