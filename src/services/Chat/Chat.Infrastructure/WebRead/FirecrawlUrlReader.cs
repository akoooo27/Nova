using Chat.Application.Abstractions.WebRead;
using Chat.Infrastructure.Options;

using Firecrawl;

using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.WebRead;

internal sealed class FirecrawlUrlReader : IUrlReader, IDisposable
{
    private const int MaxContentLength = 50_000;

    private readonly FirecrawlClient client;

    public FirecrawlUrlReader(HttpClient httpClient, IOptions<FirecrawlOptions> options)
    {
        FirecrawlOptions value = options.Value;

        client = new FirecrawlClient
        (
            apiKey: value.ApiKey,
            httpClient: httpClient,
            baseUri: value.BaseUrl,
            authorizations: [],
            disposeHttpClient: false
        );
    }

    public async Task<ReadPage> ReadAsync(Uri url, CancellationToken cancellationToken)
    {
        ScrapeResponse response = await client.Scraping.ScrapeAndExtractFromUrlAsync
        (
            url: url,
            cancellationToken: cancellationToken
        );

        string markdown = response.Data?.Markdown ?? string.Empty;

        if (markdown.Length > MaxContentLength)
        {
            markdown = markdown[..MaxContentLength];
        }

        return new ReadPage
        (
            Url: CreateUri(response.Data?.Metadata?.SourceURL) ?? url,
            Title: response.Data?.Metadata?.Title,
            Markdown: markdown
        );
    }

    private static Uri? CreateUri(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            ? uri
            : null;
    }

    public void Dispose() => client.Dispose();
}