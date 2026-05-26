using Microsoft.Extensions.DependencyInjection;

using Shared.Application.Authentication;
using Shared.Infrastructure.Authentication;
using Shared.Infrastructure.Clock;
using Shared.Infrastructure.DomainEvents;

using SharedKernel;

namespace Shared.Infrastructure;

public static class DependencyInjection
{

    public static IServiceCollection AddSharedInfrastructure(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

        services.AddScoped<IUserContext, UserContext>();

        services.AddScoped<IDomainEventsDispatcher, DomainEventsDispatcher>();

        return services;
    }
}