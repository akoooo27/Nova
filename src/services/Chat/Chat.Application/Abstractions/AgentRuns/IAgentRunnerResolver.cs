using Chat.Domain.AgentRuns.ValueObjects;

namespace Chat.Application.Abstractions.AgentRuns;

public interface IAgentRunnerResolver
{
    /// <summary>Returns the runner registered for the kind, or null when none exists.</summary>
    IAgentRunRunner? Resolve(AgentRunKind kind);
}