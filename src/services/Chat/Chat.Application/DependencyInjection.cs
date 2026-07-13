using Chat.Application.Abstractions.AgentRuns;
using Chat.Application.AgentRuns;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

using Shared.Application.Behaviors;

namespace Chat.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediator(options =>
        {
            options.ServiceLifetime = ServiceLifetime.Scoped;

            options.Assemblies =
            [
                typeof(DependencyInjection).Assembly
            ];

            options.PipelineBehaviors =
            [
                typeof(LoggingBehavior<,>),
                typeof(ValidationBehavior<,>)
            ];
        });

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        services.AddScoped<IAgentRunContextBuilder, AgentRunContextBuilder>();

        return services;
    }
}