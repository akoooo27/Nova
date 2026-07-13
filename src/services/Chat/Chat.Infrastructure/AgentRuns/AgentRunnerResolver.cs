using Chat.Application.Abstractions.AgentRuns;
using Chat.Domain.AgentRuns.ValueObjects;

using Microsoft.Extensions.DependencyInjection;

namespace Chat.Infrastructure.AgentRuns;

internal sealed class AgentRunnerResolver(IServiceProvider serviceProvider) : IAgentRunnerResolver
{
    public IAgentRunRunner? Resolve(AgentRunKind kind) =>
        serviceProvider.GetKeyedService<IAgentRunRunner>(kind);
}