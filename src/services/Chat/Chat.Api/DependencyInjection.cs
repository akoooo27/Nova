using Chat.Api.Options;
using Chat.Api.SharedChats;
using Chat.Application;
using Chat.Infrastructure;

using FastEndpoints;
using FastEndpoints.Swagger;

namespace Chat.Api;

internal static class DependencyInjection
{
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddApplication()
            .AddInfrastructure(configuration)
            .AddSharedLinks(configuration)
            .AddFastEndpointsInternal();

        return services;
    }

    private static IServiceCollection AddSharedLinks(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<SharedLinksOptions>()
            .Bind(configuration.GetSection(SharedLinksOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<SharedLinkUrlBuilder>();

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
                settings.Title = "Chat API";
                settings.Description = "API for chatting with llm models.";
                settings.Version = "v1";
            };
        });
    }
}