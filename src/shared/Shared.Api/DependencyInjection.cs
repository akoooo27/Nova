using Microsoft.Extensions.DependencyInjection;

using Shared.Api.Infrastructure;

namespace Shared.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddSharedApi(this IServiceCollection services)
    {
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        return services;
    }
}