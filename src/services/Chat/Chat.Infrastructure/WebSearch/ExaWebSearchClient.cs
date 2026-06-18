using System.Net.Http.Json;
using System.Text.Json.Serialization;

using Chat.Application.Abstractions.WebSearch;

namespace Chat.Infrastructure.WebSearch;

internal sealed class ExaWebSearchClient(HttpClient httpClient) : IWebSearchClient
{
    private const int MaxResults = 10;

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync
    (
        string query,
        int count,
        CancellationToken cancellationToken
    )
    {
        int clamped = Math.Clamp(count, 1, MaxResults);

        ExaSearchRequest request = new
        (
            Query: query,
            NumResults: clamped,
            Contents: new ExaContentsRequest(Highlights: true)
        );

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync
        (
            requestUri: "/search",
            value: request,
            cancellationToken: cancellationToken
        );
        response.EnsureSuccessStatusCode();

        ExaSearchResponse? payload = await response.Content.ReadFromJsonAsync<ExaSearchResponse>(cancellationToken);

        if (payload?.Results is null)
        {
            return [];
        }

        return payload.Results
            .Select(result => new WebSearchResult
            (
                Title: result.Title ?? result.Url,
                ReferencedSite: result.Url,
                Snippet: BestSnippet(result),
                PublishedAt: result.PublishedDate?.ToString()
            ))
            .ToList();
    }

    private static string BestSnippet(ExaResult result)
    {
        if (result.Highlights is { Length: > 0 })
        {
            return string.Join(" ", result.Highlights);
        }

        return result.Text ?? string.Empty;
    }

    private sealed record ExaSearchRequest
    (
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("numResults")] int NumResults,
        [property: JsonPropertyName("contents")] ExaContentsRequest Contents
    );

    private sealed record ExaContentsRequest
    (
        [property: JsonPropertyName("highlights")] bool Highlights
    );

    private sealed record ExaSearchResponse
    (
        [property: JsonPropertyName("results")] ExaResult[]? Results
    );

    private sealed record ExaResult
    (
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("highlights")] string[]? Highlights,
        [property: JsonPropertyName("publishedDate")] string? PublishedDate
    );
}