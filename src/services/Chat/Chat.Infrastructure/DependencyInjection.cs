using Amazon.S3;

using ArcadeDotnet;

using Chat.Application.Abstractions.Analytics;
using Chat.Application.Abstractions.Arcade;
using Chat.Application.Abstractions.Arcade.Google;
using Chat.Application.Abstractions.Database;
using Chat.Application.Abstractions.Gmail;
using Chat.Application.Abstractions.ModelCatalog;
using Chat.Application.Abstractions.ProviderLogos;
using Chat.Application.Abstractions.Turns;
using Chat.Application.Abstractions.WebRead;
using Chat.Application.Abstractions.WebSearch;
using Chat.Application.Chats.Cleanup;
using Chat.Application.Chats.Queries.GetChat;
using Chat.Application.Chats.Queries.GetChats;
using Chat.Application.Chats.Queries.SearchChats;
using Chat.Application.FavoriteModels.Queries.GetFavoriteModels;
using Chat.Application.ModelCatalog.LlmProviders.Queries.GetManagedModelCatalog;
using Chat.Application.SharedChats.Queries.GetPublicSharedChat;
using Chat.Application.SharedChats.Queries.GetSharedChats;
using Chat.Application.Turns;
using Chat.Application.Turns.Tools;
using Chat.Application.Turns.Tools.Gmail;
using Chat.Domain.Chats;
using Chat.Domain.FavoriteModels;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Events;
using Chat.Domain.SharedChats;
using Chat.Infrastructure.Agents;
using Chat.Infrastructure.Analytics;
using Chat.Infrastructure.Arcade;
using Chat.Infrastructure.Chats.Readers;
using Chat.Infrastructure.Chats.Repositories;
using Chat.Infrastructure.Database;
using Chat.Infrastructure.FavoriteModels.Readers;
using Chat.Infrastructure.FavoriteModels.Repositories;
using Chat.Infrastructure.Gmail;
using Chat.Infrastructure.ModelCatalog.Caching;
using Chat.Infrastructure.ModelCatalog.Readers;
using Chat.Infrastructure.ModelCatalog.Repositories;
using Chat.Infrastructure.Options;
using Chat.Infrastructure.ProviderLogos;
using Chat.Infrastructure.SharedChats.Readers;
using Chat.Infrastructure.SharedChats.Repositories;
using Chat.Infrastructure.Turns;
using Chat.Infrastructure.Turns.Consumers;
using Chat.Infrastructure.Users.Consumers;
using Chat.Infrastructure.WebRead;
using Chat.Infrastructure.WebSearch;

using MassTransit;

using Mediator;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using PostHog;

using Shared.Application.Messaging;
using Shared.Infrastructure;
using Shared.Infrastructure.DomainEvents;
using Shared.Infrastructure.Messaging;

using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

using PostHogSdkOptions = PostHog.PostHogOptions;

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
            .AddMessagingServices(configuration)
            .AddTurnStreamReading()
            .AddTurnStopSignal()
            .AddProviderLogoStorage(configuration)
            .AddArcadeAuth(configuration)
            .AddGoogleIntegration(configuration);

    public static IServiceCollection AddTurnWorkerInfrastructure
    (
        this IServiceCollection services,
        IConfiguration configuration
    ) =>
        services
            .AddSharedInfrastructure()
            .AddDatabaseServices()
            .AddTurnStopSignal()
            .AddTurnPipeline(configuration)
            .AddTurnWorkerMessaging(configuration);

    public static IServiceCollection AddCleanupWorkerInfrastructure(this IServiceCollection services) =>
        services
            .AddSharedInfrastructure()
            .AddDatabaseServices()
            .AddScoped<ITemporaryChatCleaner, TemporaryChatCleaner>();

    private static IServiceCollection AddDatabaseServices(this IServiceCollection services)
    {
        services.AddDomainEventsDispatcher();

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ChatDbContext>());

        services.AddScoped<ILlmProviderRepository, LlmProviderRepository>();
        services.AddScoped<IFavoriteModelRepository, FavoriteModelRepository>();
        services.AddScoped<IChatRepository, ChatRepository>();
        services.AddScoped<ISharedChatRepository, SharedChatRepository>();

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

        services
            .AddScoped<INotificationHandler<DomainEventNotification<LlmModelAvailabilityChanged>>,
                LlmModelAvailabilityChangedCacheHandler>();

        services
            .AddScoped<INotificationHandler<DomainEventNotification<LlmModelRemoved>>,
                LlmModelRemovedCacheHandler>();

        services
            .AddScoped<INotificationHandler<DomainEventNotification<LlmProviderUpdated>>,
                LlmProviderUpdatedCacheHandler>();

        services
            .AddScoped<INotificationHandler<DomainEventNotification<LlmProviderDeleted>>,
                LlmProviderDeletedCacheHandler>();

        return services;
    }

    private static IServiceCollection AddReaders(this IServiceCollection services)
    {
        services.AddScoped<PublicModelCatalogDapperReader>();
        services.AddScoped<IPublicModelCatalogReader, CachedPublicModelCatalogReader>();

        services.AddScoped<IManagedModelCatalogReader, ManagedModelCatalogDapperReader>();

        services.AddScoped<IFavoriteModelsReader, FavoriteModelsReader>();

        services.AddScoped<IChatListReader, ChatListReader>();
        services.AddScoped<IChatDetailReader, ChatDetailReader>();
        services.AddScoped<IChatSearchReader, ChatSearchReader>();

        services.AddScoped<ISharedChatListReader, SharedChatListReader>();
        services.AddScoped<IPublicSharedChatReader, PublicSharedChatReader>();

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

    private static IServiceCollection AddProviderLogoStorage(this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<ProviderLogoStorageOptions>()
            .Bind(configuration.GetSection(ProviderLogoStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());

        services.AddScoped<IProviderLogoStorage, S3ProviderLogoStorage>();

        return services;
    }

    private static IServiceCollection AddTurnStreamReading(this IServiceCollection services)
    {
        services.AddSingleton<ITurnStreamReader, RedisTurnStreamReader>();

        return services;
    }

    // Arcade authorization (options, client, auth client). Shared by the API host,
    // which exposes the user-verification endpoint, and the turn pipeline, which
    // executes Arcade-backed tools.
    private static IServiceCollection AddArcadeAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<ArcadeOptions>()
            .Bind(configuration.GetSection(ArcadeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(sp =>
        {
            ArcadeOptions options = sp.GetRequiredService<IOptions<ArcadeOptions>>().Value;

            return new ArcadeClient
            {
                APIKey = options.ApiKey,
                BaseUrl = options.BaseUrl
            };
        });

        services.AddScoped<IArcadeAuthClient, ArcadeAuthClient>();

        return services;
    }

    // User-managed Google integration (options + client). API host only: the
    // turn worker shares AddArcadeAuth but never connects integrations, so its
    // Google-specific config must not gate worker startup.
    private static IServiceCollection AddGoogleIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<GoogleIntegrationOptions>()
            .Bind(configuration.GetSection(GoogleIntegrationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IGoogleIntegrationClient, GoogleIntegrationClient>();

        return services;
    }

    private static IServiceCollection AddTurnStopSignal(this IServiceCollection services)
    {
        services.AddSingleton<ITurnStopSignal, RedisTurnStopSignal>();

        return services;
    }

    private static IServiceCollection AddTurnPipeline(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<AgentOptions>()
            .Bind(configuration.GetSection(AgentOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<ExaOptions>()
            .Bind(configuration.GetSection(ExaOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<ChatTurnOrchestrator>();
        services.AddScoped<IContextBuilder, ContextBuilder>();
        services.AddSingleton<IMemoryRetriever, NoOpMemoryRetriever>();
        services.AddSingleton<ITokenPublisher, RedisStreamTokenPublisher>();
        services.AddScoped<IAgentTool, WebSearchTool>();

        services
            .AddHttpClient<IWebSearchClient, ExaWebSearchClient>((serviceProvider, httpClient) =>
            {
                ExaOptions options = serviceProvider.GetRequiredService<IOptions<ExaOptions>>().Value;

                httpClient.BaseAddress = options.BaseUrl;
                httpClient.DefaultRequestHeaders.Add("x-api-key", options.ApiKey);
            })
            .AddStandardResilienceHandler();

        // read_url tool (Firecrawl). Delete this block to remove the tool entirely.
        services
            .AddOptions<FirecrawlOptions>()
            .Bind(configuration.GetSection(FirecrawlOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddHttpClient<IUrlReader, FirecrawlUrlReader>()
            .AddStandardResilienceHandler();

        services.AddScoped<IAgentTool, ReadUrlTool>();

        // Gmail tools (Arcade). Delete this block to remove Gmail tool access entirely.
        services.AddArcadeAuth(configuration);

        services.AddScoped<IArcadeToolExecutor, ArcadeToolExecutor>();
        services.AddScoped<IGmailToolClient, ArcadeGmailToolClient>();
        services.AddScoped<IAgentTool, GmailWhoAmITool>();

        // Decorator stack (spec Rule 3): remove this registration and AddAnalytics
        // to delete PostHog without changing the turn pipeline.
        services.AddScoped<AgentFrameworkRunner>();
        services.AddScoped<IAgentRunner>(sp => new TelemetryAgentRunner
        (
            inner: sp.GetRequiredService<AgentFrameworkRunner>(),
            analytics: sp.GetRequiredService<IAnalytics>()
        ));

        AddAnalytics(services, configuration);

        return services;
    }

    private static void AddAnalytics(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<PostHogAnalyticsOptions>()
            .Bind(configuration.GetSection(PostHogAnalyticsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        PostHogAnalyticsOptions options = configuration
            .GetSection(PostHogAnalyticsOptions.SectionName)
            .Get<PostHogAnalyticsOptions>() ?? new PostHogAnalyticsOptions();

        if (string.IsNullOrWhiteSpace(options.ProjectApiKey))
        {
            services.AddSingleton<IAnalytics, NullAnalytics>();
            return;
        }

        services.AddSingleton<IPostHogClient>(_ => new PostHogClient(new PostHogSdkOptions
        {
            ProjectToken = options.ProjectApiKey,
            HostUrl = options.HostUrl
        }));

        services.AddSingleton<IAnalytics, PostHogAnalytics>();
    }

    private static IServiceCollection AddTurnWorkerMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IMessageBus, MessageBus>();

        services.AddMassTransit(configurator =>
        {
            configurator.SetKebabCaseEndpointNameFormatter();

            configurator.AddConsumer<TurnRequestedConsumer, TurnRequestedConsumerDefinition>();

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