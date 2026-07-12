using Chat.Application.Turns;

namespace Chat.Application.Tests.AgentRuns;

public sealed class AgentActivityEventSerializerTests
{
    private static readonly Guid TurnId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void RoundTripsWithStableDiscriminator()
    {
        AgentActivityEvent original = new
        (
            TurnId: TurnId,
            Sequence: 7,
            Kind: "ToolCall",
            Type: "web.search",
            Title: "Searching: EU battery regulation 2026",
            DetailJson: "{\"query\":\"EU battery regulation 2026\"}"
        );

        string json = TurnEventSerializer.Serialize(original);

        Assert.Contains("\"type\":\"agent_activity\"", json, StringComparison.Ordinal);
        Assert.Equal("agent_activity", TurnEventSerializer.EventName(original));
        Assert.Equal(original, TurnEventSerializer.Deserialize(json));
    }

    [Fact]
    public void RoundTripsWithNullDetail()
    {
        AgentActivityEvent original = new(TurnId, Sequence: 1, Kind: "Phase", Type: "phase", Title: "Planning", DetailJson: null);

        Assert.Equal(original, TurnEventSerializer.Deserialize(TurnEventSerializer.Serialize(original)));
    }
}