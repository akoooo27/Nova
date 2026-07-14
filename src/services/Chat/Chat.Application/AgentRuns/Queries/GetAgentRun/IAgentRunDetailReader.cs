using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Application.AgentRuns.Queries.GetAgentRun;

/// <summary>
/// Reads one agent run's full card data (summary + ordered activities) for the owner.
/// Owner + chat scoping is enforced in SQL; a mismatch returns null, indistinguishable
/// from absence (no information leak).
/// </summary>
public interface IAgentRunDetailReader
{
    Task<AgentRunDetailResult?> GetAsync
    (
        ChatId chatId,
        ChatMessageId messageId,
        UserId userId,
        CancellationToken cancellationToken = default
    );
}
