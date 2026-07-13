using Chat.Application.Abstractions.AgentRuns;
using Chat.Domain.AgentRuns.ValueObjects;

namespace Chat.Application.Tests.AgentRuns;

internal sealed class FakeAgentRunnerResolver(IAgentRunRunner? runner) : IAgentRunnerResolver
{
    public IAgentRunRunner? Resolve(AgentRunKind kind) => runner;
}