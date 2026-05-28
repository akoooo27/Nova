using FastEndpoints;

using IdentityIngress.Database;

using MassTransit;

using Microsoft.Extensions.DependencyInjection;

using Shared.Api;
using Shared.Infrastructure;
using Shared.Infrastructure.Messaging;

using SharedKernel.Application.Messaging;

namespace IdentityIngress;

internal static class DependencyInjection
{
    public static IServiceCollection AddApi(this IServiceCollection services)
    {
        services
            .AddSharedInfra()
            .AddMassTransitInternal()
            .AddSharedApi()
            .AddFastEndpoints();

        return services;
    }

    private static IServiceCollection AddSharedInfra(this IServiceCollection services)
    {
        return services.AddSharedInfrastructure();
    }

    private static IServiceCollection AddMassTransitInternal(this IServiceCollection services)
    {
        services.AddScoped<IMessageBus, MessageBus>();

        services.AddMassTransit(configurator =>
        {
            configurator.SetKebabCaseEndpointNameFormatter();

            configurator.AddEntityFrameworkOutbox<IdentityIngressDbContext>(outbox =>
            {
                outbox.UsePostgres();
                outbox.UseBusOutbox();
            });

            configurator.AddConfigureEndpointsCallback((context, _, endpointConfigurator) =>
            {
                endpointConfigurator.UseEntityFrameworkOutbox<IdentityIngressDbContext>(context);
            });

            configurator.UsingRabbitMq((context, rabbitMqConfigurator) =>
            {
                string rabbitMqConnectionString = context.GetRequiredService<IConfiguration>()
                    .GetConnectionString("rabbitmq")
                    ?? throw new InvalidOperationException("Connection string 'rabbitmq' is required.");

                rabbitMqConfigurator.Host(new Uri(rabbitMqConnectionString));
                rabbitMqConfigurator.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}