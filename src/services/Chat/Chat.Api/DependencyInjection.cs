using Chat.Application;
using Chat.Infrastructure;

namespace Chat.Api;

internal static class DependencyInjection
{
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddApplication()
            .AddInfrastructure(configuration);

        return services;
    }

}