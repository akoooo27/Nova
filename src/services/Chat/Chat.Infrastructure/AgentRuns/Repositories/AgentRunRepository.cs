using Chat.Domain.AgentRuns;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Domain.Chats.ValueObjects;
using Chat.Infrastructure.Database;

using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.AgentRuns.Repositories;

internal sealed class AgentRunRepository(ChatDbContext db) : IAgentRunRepository
{
    public async Task<AgentRun?> GetByIdAsync(AgentRunId id, CancellationToken cancellationToken = default)
    {
        return await db.AgentRuns
            .Include(x => x.Activities.OrderBy(activity => activity.Sequence))
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<AgentRun?> GetByAssistantMessageIdAsync(ChatMessageId assistantMessageId, CancellationToken cancellationToken = default)
    {
        return await db.AgentRuns
            .Include(x => x.Activities.OrderBy(activity => activity.Sequence))
            .FirstOrDefaultAsync(x => x.AssistantMessageId == assistantMessageId, cancellationToken);
    }

    public void Add(AgentRun run)
    {
        db.AgentRuns.Add(run);
    }
}