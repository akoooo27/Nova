using Duende.Bff.AccessTokenManagement;
using Duende.Bff.Yarp;

using Yarp.ReverseProxy.Configuration;

namespace BFF.RemoteApis;

internal static class ChatApiProxyConfiguration
{
    private const string AddressConfigurationKey = "ChatApi:Address";
    private const string DefaultAddress = "https://localhost:7201";

    private const string RouteId = "chat-api";
    private const string ClusterId = "chat-api";
    private const string DestinationId = "chat-api";

    public static string GetAddress(IConfiguration configuration) =>
        configuration[AddressConfigurationKey] ?? DefaultAddress;

    public static RouteConfig CreateRoute() =>
        new RouteConfig
        {
            RouteId = RouteId,
            ClusterId = ClusterId,
            Match = new RouteMatch
            {
                Path = "/api/chat/{**catch-all}",
            },
            Transforms =
            [
                new Dictionary<string, string>
                {
                    ["PathPattern"] = "/{**catch-all}",
                },
            ],
        }
        .WithAccessToken(RequiredTokenType.User)
        .WithAntiforgeryCheck();

    public static ClusterConfig CreateCluster(string address) =>
        new()
        {
            ClusterId = ClusterId,
            Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
            {
                [DestinationId] = new()
                {
                    Address = address,
                },
            },
        };
}