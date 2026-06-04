using Chat.Application.Abstractions.Database;
using Chat.Application.Abstractions.ModelCatalog;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Events;
using Chat.Infrastructure.Database;
using Chat.Infrastructure.ModelCatalog.Caching;
using Chat.Infrastructure.ModelCatalog.Readers;
using Chat.Infrastructure.ModelCatalog.Repositories;
using Chat.Infrastructure.Users.Consumers;

using MassTransit;

using Mediator;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Shared.Application.Messaging;
using Shared.Infrastructure;
using Shared.Infrastructure.DomainEvents;
using Shared.Infrastructure.Messaging;

using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace Chat.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection
        AddInfrastructure(this IServiceCollection services, IConfiguration configuration) =>
        services
            .AddSharedInfrastructure()
            .AddAuth0JwtAuthentication(configuration)
            .AddDatabaseServices()
            .AddCacheServices(configuration)
            .AddReaders()
            .AddMessagingServices(configuration);

    private static IServiceCollection AddDatabaseServices(this IServiceCollection services)
    {
        services.AddDomainEventsDispatcher();

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ChatDbContext>());

        services.AddScoped<ILlmProviderRepository, LlmProviderRepository>();

        return services;
    }

    private static IServiceCollection AddCacheServices(this IServiceCollection services, IConfiguration configuration)
    {
        string redisConnectionString = configuration.GetConnectionString("redis")
                                       ?? throw new InvalidOperationException("Connection string 'redis' is required.");

        services.AddFusionCache()
            .WithDefaultEntryOptions(options =>
            {
                options.Duration = TimeSpan.FromMinutes(5);
                options.FailSafeMaxDuration = TimeSpan.FromMinutes(30);
                options.FactorySoftTimeout = TimeSpan.FromMilliseconds(150);
                options.FactoryHardTimeout = TimeSpan.FromSeconds(2);
            })
            .WithSerializer(new FusionCacheSystemTextJsonSerializer())
            .WithRegisteredDistributedCache()
            .WithBackplane(new RedisBackplane(new RedisBackplaneOptions
            {
                Configuration = redisConnectionString
            }));

        services
            .AddScoped<INotificationHandler<DomainEventNotification<LlmModelProfileUpdated>>,
                LlmModelProfileUpdatedCacheHandler>();

        return services;
    }

    private static IServiceCollection AddReaders(this IServiceCollection services)
    {
        services.AddScoped<PublicModelCatalogDapperReader>();
        services.AddScoped<IPublicModelCatalogReader, CachedPublicModelCatalogReader>();

        return services;
    }

    private static IServiceCollection AddMessagingServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IMessageBus, MessageBus>();

        services.AddMassTransit(configurator =>
        {
            configurator.SetKebabCaseEndpointNameFormatter();

            configurator.AddConsumer<UserRegisteredConsumer>();
            configurator.AddConsumer<UserUpdatedConsumer>();
            configurator.AddConsumer<UserDeletedConsumer>();

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