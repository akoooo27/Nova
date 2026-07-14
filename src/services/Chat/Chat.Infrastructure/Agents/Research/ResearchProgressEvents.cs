using Microsoft.Agents.AI.Workflows;

namespace Chat.Infrastructure.Agents.Research;

internal sealed record ResearchProgress
(
    int Sequence,
    string Kind,
    string Type,
    string Title,
    string? DetailJson
);

internal sealed class ResearchProgressEvent(ResearchProgress progress) : WorkflowEvent
{
    public ResearchProgress Progress { get; } = progress;
}

internal sealed class ResearchUsageEvent(int inputTokens, int outputTokens) : WorkflowEvent
{
    public int InputTokens { get; } = inputTokens;

    public int OutputTokens { get; } = outputTokens;
}