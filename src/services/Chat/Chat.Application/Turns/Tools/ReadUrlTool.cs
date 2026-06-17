using System.ComponentModel;

using Chat.Application.Abstractions.Turns;
using Chat.Application.Abstractions.WebRead;

namespace Chat.Application.Turns.Tools;

public sealed class ReadUrlTool(IUrlReader reader) : IAgentTool
{
    private const string UnavailableMessage =
        "That page could not be read. Tell the user you couldn't open the URL and continue with what you know.";

    public string Name => AgentToolNames.ReadUrl;

    public Delegate CreateInvocation() => ReadAsync;

    [Description("Fetch the full readable content of a specific web page as markdown. " +
        "Use to read a URL the user gave you, or a result returned by web_search.")]
    private async Task<ReadUrlResponse> ReadAsync
    (
        [Description("Absolute http(s) URL of the page to read.")] string url,
        CancellationToken cancellationToken = default
    )
    {
        Uri? uri = CreateHttpUri(url);

        if (uri is null)
        {
            return new ReadUrlResponse
            (
                Available: false,
                Page: null,
                Note: "The URL must be an absolute http or https address."
            );
        }

        try
        {
            ReadPage page = await reader.ReadAsync(uri, cancellationToken);

            return new ReadUrlResponse
            (
                Available: true,
                Page: page,
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
            return new ReadUrlResponse
            (
                Available: false,
                Page: null,
                Note: UnavailableMessage
            );
        }
    }

    private static Uri? CreateHttpUri(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps
            ? uri
            : null;
    }
}