using Chat.Application.AgentRuns.Queries.GetAgentRun;
using Chat.Application.Tests.FavoriteModels;

using ErrorOr;

namespace Chat.Application.Tests.AgentRuns;

public sealed class GetAgentRunHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeAgentRunDetailReader _reader = new();

    private (Guid ChatId, Guid MessageId) SeedRun(string userId = "auth0|user-1")
    {
        Guid chatId = Guid.CreateVersion7();
        Guid messageId = Guid.CreateVersion7();

        AgentRunDetailResult detail = new
        (
            Kind: "Research",
            Task: "Research Redis Streams",
            CurrentPhase: "Planning",
            StartedAt: Now,
            FinishedAt: null,
            Usage: new AgentRunUsageResult(0, 0),
            Activities:
            [
                new AgentRunActivityResult(1, "Phase", "phase", "Planning", null, Now),
                new AgentRunActivityResult(2, "ToolCall", "web.search", "Searching: redis streams", "{\"query\":\"redis streams\"}", Now)
            ]
        );

        _reader.Seed(chatId, messageId, userId, detail);
        return (chatId, messageId);
    }

    private GetAgentRunHandler CreateHandler(string userId = "auth0|user-1") =>
        new(userContext: new FakeUserContext(userId), reader: _reader);

    [Fact]
    public async Task HandleReturnsSummaryAndOrderedActivities()
    {
        (Guid chatId, Guid messageId) = SeedRun();

        ErrorOr<AgentRunDetailResult> result = await CreateHandler()
            .Handle(new GetAgentRunQuery(chatId, messageId), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Research", result.Value.Kind);
        Assert.Equal("Research Redis Streams", result.Value.Task);
        Assert.Equal("Planning", result.Value.CurrentPhase);
        Assert.Equal(2, result.Value.Activities.Count);
        Assert.Equal(1, result.Value.Activities[0].Sequence);
        Assert.Equal("web.search", result.Value.Activities[1].Type);
        Assert.Equal("{\"query\":\"redis streams\"}", result.Value.Activities[1].Detail);
    }

    [Fact]
    public async Task HandleWhenCallerIsNotTheOwnerReturnsNotFound()
    {
        (Guid chatId, Guid messageId) = SeedRun(userId: "auth0|owner");

        ErrorOr<AgentRunDetailResult> result = await CreateHandler(userId: "auth0|other")
            .Handle(new GetAgentRunQuery(chatId, messageId), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("AgentRun.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task HandleWhenNoRunForMessageReturnsNotFound()
    {
        ErrorOr<AgentRunDetailResult> result = await CreateHandler()
            .Handle(new GetAgentRunQuery(Guid.CreateVersion7(), Guid.CreateVersion7()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("AgentRun.NotFound", result.FirstError.Code);
    }
}