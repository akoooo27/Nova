using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Domain.Chats.ValueObjects;

namespace Chat.Domain.AgentRuns;

public interface IAgentRunRepository
{
    Task<AgentRun?> GetByIdAsync(AgentRunId id, CancellationToken cancellationToken = default);

    Task<AgentRun?> GetByAssistantMessageIdAsync(ChatMessageId assistantMessageId,
        CancellationToken cancellationToken = default);

    void Add(AgentRun run);
}