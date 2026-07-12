using Chat.Domain.AgentRuns;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Domain.Chats.ValueObjects;

namespace Chat.Application.Tests.AgentRuns;

internal sealed class FakeAgentRunRepository : IAgentRunRepository
{
    private readonly List<AgentRun> _runs = [];

    public IReadOnlyList<AgentRun> Runs => _runs;

    public void Add(AgentRun run)
    {
        _runs.Add(run);
    }

    public Task<AgentRun?> GetByIdAsync(AgentRunId id, CancellationToken cancellationToken = default)
    {
        AgentRun? run = _runs.FirstOrDefault(x => x.Id == id);

        return Task.FromResult(run);
    }

    public Task<AgentRun?> GetByAssistantMessageIdAsync
    (
        ChatMessageId assistantMessageId,
        CancellationToken cancellationToken = default
    )
    {
        AgentRun? run = _runs.FirstOrDefault(x => x.AssistantMessageId == assistantMessageId);

        return Task.FromResult(run);
    }
}