using Chat.Api.Options;

using Microsoft.Extensions.Options;

namespace Chat.Api.SharedChats;

internal sealed class SharedLinkUrlBuilder(IOptions<SharedLinksOptions> options)
{
    private readonly Uri _baseUri = new(options.Value.PublicBaseUrl.TrimEnd('/') + "/");

    public string Build(Guid sharedChatId) =>
        new Uri(_baseUri, $"share/{sharedChatId}").AbsoluteUri;
}