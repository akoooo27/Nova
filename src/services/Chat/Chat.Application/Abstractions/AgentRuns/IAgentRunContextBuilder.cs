using Chat.Domain.AgentRuns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;

using ErrorOr;

namespace Chat.Application.Abstractions.AgentRuns;

public interface IAgentRunContextBuilder
{
    Task<ErrorOr<AgentRunContext>> BuildContextAsync
    (
        ChatThread thread,
        ChatMessage assistantMessage,
        AgentRun run,
        CancellationToken cancellationToken
    );
}