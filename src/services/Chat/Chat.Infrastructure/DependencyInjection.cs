using Chat.Application.Abstractions.Database;
using Chat.Domain.ModelCatalog;
using Chat.Infrastructure.Database;
using Chat.Infrastructure.Database.Repositories;

using MassTransit;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Shared.Application.Messaging;
using Shared.Infrastructure;
using Shared.Infrastructure.Messaging;

namespace Chat.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection
        AddInfrastructure(this IServiceCollection services, IConfiguration configuration) =>
        services
            .AddSharedInfrastructure()
            .AddDatabaseServices()
            .AddMessagingServices(configuration);

    private static IServiceCollection AddDatabaseServices(this IServiceCollection services)
    {
        services.AddDomainEventsDispatcher();

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ChatDbContext>());

        services.AddScoped<ILlmProviderRepository, LlmProviderRepository>();

        return services;
    }

    private static IServiceCollection AddMessagingServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IMessageBus, MessageBus>();

        services.AddMassTransit(configurator =>
        {
            configurator.SetKebabCaseEndpointNameFormatter();

            configurator.AddEntityFrameworkOutbox<ChatDbContext>(outbox =>
            {
                outbox.UsePostgres();
                outbox.UseBusOutbox();
            });

            configurator.AddConfigureEndpointsCallback((context, _, endpointConfigurator) =>
            {
                endpointConfigurator.UseEntityFrameworkOutbox<ChatDbContext>(context);
            });

            configurator.UsingRabbitMq((context, rabbitMqConfigurator) =>
            {
                string rabbitMqConnectionString = configuration.GetConnectionString("rabbitmq")
                    ?? throw new InvalidOperationException("Connection string 'rabbitmq' is required.");

                rabbitMqConfigurator.Host(new Uri(rabbitMqConnectionString));
                rabbitMqConfigurator.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}