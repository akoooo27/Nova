using Chat.Application.Abstractions.Turns;
using Chat.Application.Abstractions.WebRead;
using Chat.Application.Turns.Tools;

namespace Chat.Application.Tests.Turns;

public sealed class ReadUrlToolTests
{
    private static readonly TurnToolContext ToolContext = new
    (
        TurnId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        ChatId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
        UserId: "auth0|user-1"
    );

    [Fact]
    public async Task CreateInvocationReturnsReadPageFromReader()
    {
        ReadPage page = new
        (
            Url: new Uri("https://example.com/docs"),
            Title: "Example Docs",
            Markdown: "# Example"
        );
        FakeUrlReader reader = new(page);
        ReadUrlTool tool = new(reader);

        ReadUrlResponse response = await InvokeAsync
        (
            tool,
            url: "https://example.com/docs",
            cancellationToken: CancellationToken.None
        );

        Assert.True(response.Available);
        Assert.Null(response.Note);
        Assert.Equal(page, response.Page);
        Assert.Equal(1, reader.Calls);
        Assert.Equal(new Uri("https://example.com/docs"), reader.Url);
    }

    [Theory]
    [InlineData("example.com/docs")]
    [InlineData("ftp://example.com/docs")]
    [InlineData("not a url")]
    public async Task CreateInvocationWhenUrlIsNotAbsoluteHttpUrlReturnsUnavailableResponse(string input)
    {
        FakeUrlReader reader = new();
        ReadUrlTool tool = new(reader);

        ReadUrlResponse response = await InvokeAsync
        (
            tool,
            url: input,
            cancellationToken: CancellationToken.None
        );

        Assert.False(response.Available);
        Assert.Null(response.Page);
        Assert.Equal("The URL must be an absolute http or https address.", response.Note);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task CreateInvocationWhenReaderThrowsReturnsUnavailableResponse()
    {
        FakeUrlReader reader = new(exception: new InvalidOperationException("provider down"));
        ReadUrlTool tool = new(reader);

        ReadUrlResponse response = await InvokeAsync
        (
            tool,
            url: "https://example.com/docs",
            cancellationToken: CancellationToken.None
        );

        Assert.False(response.Available);
        Assert.Null(response.Page);
        Assert.Equal
        (
            "That page could not be read. Tell the user you couldn't open the URL and continue with what you know.",
            response.Note
        );
    }

    [Fact]
    public async Task CreateInvocationWhenReaderCancelsRethrowsCancellation()
    {
        FakeUrlReader reader = new(exception: new OperationCanceledException());
        ReadUrlTool tool = new(reader);

        await Assert.ThrowsAsync<OperationCanceledException>(() => InvokeAsync
        (
            tool,
            url: "https://example.com/docs",
            cancellationToken: CancellationToken.None
        ));
    }

    private static Task<ReadUrlResponse> InvokeAsync
    (
        ReadUrlTool tool,
        string url,
        CancellationToken cancellationToken
    )
    {
        object? result = tool.CreateInvocation(ToolContext).DynamicInvoke(url, cancellationToken);

        return Assert.IsAssignableFrom<Task<ReadUrlResponse>>(result);
    }
}