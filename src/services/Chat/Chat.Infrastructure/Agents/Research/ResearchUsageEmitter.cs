using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace Chat.Infrastructure.Agents.Research;

internal static class ResearchUsageEmitter
{
    public static async ValueTask EmitAsync(IWorkflowContext context, AgentResponse response, CancellationToken cancellationToken)
    {
        if (response.Usage is { } usage)
        {
            ResearchUsageEvent usageEvent = new
            (
                inputTokens: (int)(usage.InputTokenCount ?? 0),
                outputTokens: (int)(usage.OutputTokenCount ?? 0)
            );

            await context.AddEventAsync(usageEvent, cancellationToken);
        }
    }
}