using System.Diagnostics;
using System.Runtime.CompilerServices;

using Chat.Application.Abstractions.Analytics;
using Chat.Application.Abstractions.Turns;

namespace Chat.Application.Turns;

/// <summary>
/// Pass-through decorator (spec Rule 3): zero added latency on the token path; analytics fire
/// after the run. Deleting its DI registration removes PostHog with no other change.
/// If the inner runner throws, nothing is captured — failures are visible in logs and message state.
/// </summary>
public class TelemetryAgentRunner(IAgentRunner inner, IAnalytics analytics) : IAgentRunner
{
    private const string AiGenerationEventName = "$ai_generation";
    private const string ToolUsedEventName = "tool_used";

    public async IAsyncEnumerable<TurnEvent> RunAsync
    (
        TurnContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        long startTimestamp = Stopwatch.GetTimestamp();
        List<string> tools = [];
        UsageEvent? usage = null;

        await foreach (TurnEvent turnEvent in inner.RunAsync(context, cancellationToken))
        {
            if (turnEvent is ToolCallEvent toolCall)
            {
                tools.Add(toolCall.Tool);
            }

            if (turnEvent is UsageEvent usageEvent)
            {
                usage = usageEvent;
            }

            yield return turnEvent;
        }

        // PostHog's LLM analytics schema → cost/latency/model dashboards out of the box.
        Dictionary<string, object> properties = new()
        {
            ["$ai_model"] = usage?.Model ?? context.ExternalModelId,
            ["$ai_input_tokens"] = usage?.InputTokens ?? 0,
            ["$ai_output_tokens"] = usage?.OutputTokens ?? 0,
            ["$ai_latency"] = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
            ["$ai_trace_id"] = context.TurnId.ToString(),
            ["conversation_id"] = context.ChatId.ToString(),
            ["tools_used"] = tools
        };

        analytics.Capture
        (
            distinctId: context.UserId,
            AiGenerationEventName,
            properties
        );

        foreach (string tool in tools)
        {
            Dictionary<string, object> toolProperties = new()
            {
                ["tool"] = tool,
                ["model"] = usage?.Model ?? context.ExternalModelId,
                ["conversation_id"] = context.ChatId.ToString()
            };

            analytics.Capture
            (
                distinctId: context.UserId,
                eventName: ToolUsedEventName,
                properties: toolProperties
            );
        }
    }
}