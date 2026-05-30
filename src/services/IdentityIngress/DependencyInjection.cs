using FastEndpoints;
using FastEndpoints.Swagger;

using IdentityIngress.Database;
using IdentityIngress.Endpoints.Auth0Events;
using IdentityIngress.IdentityProviders;
using IdentityIngress.IdentityProviders.Auth0;
using IdentityIngress.Options;

using MassTransit;

using Shared.Api;
using Shared.Application.Messaging;
using Shared.Infrastructure;
using Shared.Infrastructure.Messaging;

namespace IdentityIngress;

internal static class DependencyInjection
{
    public static IServiceCollection AddApi(this IServiceCollection services)
    {
        services
            .AddSharedInfra()
            .AddMassTransitInternal()
            .AddSharedApi();

        services.AddIdentityProviderEventIngestion();
        services.AddFastEndpointsInternal();

        return services;
    }

    private static void AddIdentityProviderEventIngestion(this IServiceCollection services)
    {
        services.AddOptions<Auth0EventsOptions>()
            .BindConfiguration(Auth0EventsOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IIdentityProviderEventMapper<Request>, Auth0EventMapper>();
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

    private static void AddFastEndpointsInternal(this IServiceCollection services)
    {
        services.AddFastEndpoints(options =>
        {
            options.Assemblies =
            [
                typeof(DependencyInjection).Assembly
            ];
        });

        services.SwaggerDocument(options =>
        {
            options.MaxEndpointVersion = 1;
            options.DocumentSettings = settings =>
            {
                settings.Title = "Identity Ingress API";
                settings.Description = "Identity provider ingress anti-corruption API.";
                settings.Version = "v1";
            };
        });
    }
}