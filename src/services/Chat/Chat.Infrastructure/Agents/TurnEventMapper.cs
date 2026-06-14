using System.Text.Json;

using Chat.Application.Turns;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Chat.Infrastructure.Agents;

internal static class TurnEventMapper
{
    private const int ToolResultSummaryMaxLength = 512;

    public static IEnumerable<TurnEvent> Map
    (
        Guid turnId,
        string modelId,
        AgentResponseUpdate update
    )
    {
        foreach (AIContent content in update.Contents)
        {
            switch (content)
            {
                case TextContent text when !string.IsNullOrEmpty(text.Text):
                    TokenEvent tokenEvent = new(turnId, text.Text);

                    yield return tokenEvent;

                    break;

                case FunctionCallContent call:
                    ToolCallEvent toolCallEvent = new
                    (
                        TurnId: turnId,
                        Tool: call.Name,
                        ArgsJson: JsonSerializer.Serialize(call.Arguments)
                    );

                    yield return toolCallEvent;

                    break;

                case FunctionResultContent result:
                    string truncated = Truncate(result.Result?.ToString());

                    ToolResultEvent toolResultEvent = new
                    (
                        TurnId: turnId,
                        Tool: result.CallId,
                        Summary: truncated
                    );

                    yield return toolResultEvent;

                    break;

                case UsageContent usage:
                    UsageEvent usageEvent = new
                    (
                        TurnId: turnId,
                        Model: modelId,
                        InputTokens: (int)(usage.Details.InputTokenCount ?? 0),
                        OutputTokens: (int)(usage.Details.OutputTokenCount ?? 0)
                    );

                    yield return usageEvent;

                    break;

                case TextReasoningContent reasoning when !string.IsNullOrWhiteSpace(reasoning.Text):
                    ReasoningEvent reasoningEvent = new
                    (
                        TurnId: turnId,
                        Text: reasoning.Text
                    );

                    yield return reasoningEvent;

                    break;
            }
        }
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= ToolResultSummaryMaxLength ? value : value[..ToolResultSummaryMaxLength];
    }
}