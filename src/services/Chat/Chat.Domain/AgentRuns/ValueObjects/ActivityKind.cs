namespace Chat.Domain.AgentRuns.ValueObjects;

#pragma warning disable CA1008
public enum ActivityKind
{
    Phase = 1,
    Thought = 2,
    ToolCall = 3,
    Observation = 4,
    Error = 5
}