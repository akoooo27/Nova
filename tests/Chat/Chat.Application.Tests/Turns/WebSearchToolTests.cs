using Chat.Application.Abstractions.Turns;
using Chat.Application.Abstractions.WebSearch;
using Chat.Application.Turns.Tools;

namespace Chat.Application.Tests.Turns;

public sealed class WebSearchToolTests
{
    private static readonly TurnToolContext ToolContext = new
    (
        TurnId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        ChatId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
        UserId: "auth0|user-1"
    );

    [Fact]
    public async Task CreateInvocationReturnsSearchResultFromClient()
    {
        WebSearchResult result = new
        (
            Title: "Redis",
            ReferencedSite: "https://redis.io",
            Snippet: "Redis is an in-memory data store.",
            PublishedAt: "2026-06-13"
        );
        FakeWebSearchClient client = new([result]);
        WebSearchTool tool = new(client);

        WebSearchResponse response = await InvokeAsync
        (
            tool,
            query: "what is redis",
            count: 3,
            cancellationToken: CancellationToken.None
        );

        Assert.True(response.Available);
        Assert.Null(response.Note);
        Assert.Equal([result], response.Results);
        Assert.Equal(1, client.Calls);
        Assert.Equal("what is redis", client.Query);
        Assert.Equal(3, client.Count);
    }

    [Fact]
    public async Task CreateInvocationWhenClientThrowsReturnsUnavailableResponse()
    {
        FakeWebSearchClient client = new(exception: new InvalidOperationException("provider down"));
        WebSearchTool tool = new(client);

        WebSearchResponse response = await InvokeAsync
        (
            tool,
            query: "latest llm research",
            count: 5,
            cancellationToken: CancellationToken.None
        );

        Assert.False(response.Available);
        Assert.Empty(response.Results);
        Assert.Equal
        (
            "Web search is currently unavailable. Answer using your existing knowledge and note that you could not search the web.",
            response.Note
        );
    }

    [Fact]
    public async Task CreateInvocationWhenClientCancelsRethrowsCancellation()
    {
        FakeWebSearchClient client = new(exception: new OperationCanceledException());
        WebSearchTool tool = new(client);

        await Assert.ThrowsAsync<OperationCanceledException>(() => InvokeAsync
        (
            tool,
            query: "latest llm research",
            count: 5,
            cancellationToken: CancellationToken.None
        ));
    }

    private static Task<WebSearchResponse> InvokeAsync
    (
        WebSearchTool tool,
        string query,
        int count,
        CancellationToken cancellationToken
    )
    {
        object? result = tool.CreateInvocation(ToolContext).DynamicInvoke(query, count, cancellationToken);

        return Assert.IsAssignableFrom<Task<WebSearchResponse>>(result);
    }
}