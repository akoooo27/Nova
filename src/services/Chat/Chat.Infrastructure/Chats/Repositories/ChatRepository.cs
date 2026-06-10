using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;
using Chat.Infrastructure.Database;

using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.Chats.Repositories;

internal sealed class ChatRepository(ChatDbContext db) : IChatRepository
{
    public async Task<ChatThread?> GetByIdAsync(ChatId id, UserId userId, CancellationToken cancellationToken = default)
    {
        return await db.ChatThreads
            .Include(x => x.Messages)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);
    }

    public void Add(ChatThread chat)
    {
        db.ChatThreads.Add(chat);
    }
}