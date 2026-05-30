using Yarp.ReverseProxy.Configuration;

namespace BFF.FrontendProxy;

internal static class FrontendProxyConfiguration
{
    private const string AddressConfigurationKey = "FrontendProxy:Address";

    private const string DefaultFrontendAddress = "http://localhost:5173";
    private const string FrontendRouteId = "frontend-fallback";
    private const string FrontendClusterId = "frontend";
    private const string FrontendDestinationId = "vite";

    public static string GetFrontendAddress(IConfiguration configuration) =>
        configuration[AddressConfigurationKey] ?? DefaultFrontendAddress;

    public static IReadOnlyList<RouteConfig> CreateRoutes() =>
    [
        new RouteConfig
        {
            RouteId = FrontendRouteId,
            ClusterId = FrontendClusterId,
            Order = int.MaxValue,
            Match = new RouteMatch
            {
                Path = "/{**catch-all}",
            },
        },
    ];

    public static IReadOnlyList<ClusterConfig> CreateClusters(string frontendAddress) =>
    [
        new ClusterConfig
        {
            ClusterId = FrontendClusterId,
            Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
            {
                [FrontendDestinationId] = new DestinationConfig
                {
                    Address = frontendAddress,
                },
            },
        },
    ];
}