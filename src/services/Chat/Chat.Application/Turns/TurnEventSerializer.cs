using System.Text.Json;

namespace Chat.Application.Turns;

public static class TurnEventSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(TurnEvent turnEvent) =>
        JsonSerializer.Serialize(turnEvent, Options);

    public static TurnEvent? Deserialize(string json) =>
        JsonSerializer.Deserialize<TurnEvent>(json, Options);

    public static string EventName(TurnEvent turnEvent) => turnEvent switch
    {
        TokenEvent => "token",
        ToolCallEvent => "tool_call",
        ToolResultEvent => "tool_result",
        UsageEvent => "usage",
        DoneEvent => "done",
        FailedEvent => "failed",
        ReasoningEvent => "reasoning",
        _ => throw new ArgumentOutOfRangeException(nameof(turnEvent), turnEvent.GetType().Name, "Unknown turn event type.")
    };
}