using Chat.Application.Turns;

namespace Chat.Application.Tests.Turns;

public sealed class TurnEventSerializerTests
{
    private static readonly Guid TurnId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static TheoryData<TurnEvent, string> Events => new()
    {
        { new TokenEvent(TurnId, "Hello"), "token" },
        { new ToolCallEvent(TurnId, "search", "{}"), "tool_call" },
        { new ToolResultEvent(TurnId, "search", "3 results"), "tool_result" },
        { new UsageEvent(TurnId, "gpt-4.1", 120, 45), "usage" },
        { new DoneEvent(TurnId), "done" },
        { new FailedEvent(TurnId, "provider timeout"), "failed" },
        { new ReasoningEvent(TurnId, "The model is thinking through the problem."), "reasoning" }
    };

    [Theory]
    [MemberData(nameof(Events))]
    public void RoundTripsEveryEventTypeWithStableDiscriminator(TurnEvent original, string expectedName)
    {
        string json = TurnEventSerializer.Serialize(original);

        Assert.Contains($"\"type\":\"{expectedName}\"", json, StringComparison.Ordinal);
        Assert.Equal(expectedName, TurnEventSerializer.EventName(original));

        TurnEvent? deserialized = TurnEventSerializer.Deserialize(json);

        Assert.Equal(original, deserialized);
    }
}