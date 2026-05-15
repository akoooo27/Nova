using Microsoft.Extensions.DependencyInjection;

using Shared.Infrastructure.Time;

using SharedKernel;

namespace Shared.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSharedInfra(this IServiceCollection services)
    {
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

        return services;
    }
}