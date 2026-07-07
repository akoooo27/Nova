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

    public async Task<ChatThread?> GetSnapshotByIdAsync(ChatId id, UserId userId, CancellationToken cancellationToken = default)
    {
        return await db.ChatThreads
            .AsNoTracking()
            .Include(x => x.Messages)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);
    }

    public void Add(ChatThread chat)
    {
        db.ChatThreads.Add(chat);
    }

    public async Task<int> DeleteExpiredTemporaryChatsAsync
    (
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default
    )
    {
        const int batchSize = 1000;

        int totalDeleted = 0;

        while (true)
        {
            List<ChatId> batch = await db.ChatThreads
                .Where(chat => chat.IsTemporary && chat.UpdatedAt < olderThan)
                .OrderBy(chat => chat.UpdatedAt)
                .Take(batchSize)
                .Select(chat => chat.Id)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            totalDeleted += await db.ChatThreads
                .Where(chat => batch.Contains(chat.Id) && chat.IsTemporary && chat.UpdatedAt < olderThan)
                .ExecuteDeleteAsync(cancellationToken);

            if (batch.Count < batchSize)
            {
                break;
            }
        }

        return totalDeleted;
    }

    public async Task<int> DeleteByIdAsync
    (
        ChatId id,
        UserId userId,
        CancellationToken cancellationToken = default
    )
    {
        return await db.ChatThreads
            .Where(chat => chat.Id == id && chat.UserId == userId && !chat.IsTemporary)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> ArchiveAllAsync
    (
        UserId userId,
        CancellationToken cancellationToken = default
    )
    {
        return await db.ChatThreads
            .Where(chat => chat.UserId == userId && !chat.IsTemporary && !chat.IsArchived)
            .ExecuteUpdateAsync
            (
                setters => setters.SetProperty(chat => chat.IsArchived, true),
                cancellationToken
            );
    }

    public async Task<int> DeleteAllAsync
    (
        UserId userId,
        bool includeTemporary = false,
        CancellationToken cancellationToken = default
    )
    {
        return await db.ChatThreads
            .Where(chat => chat.UserId == userId && (includeTemporary || !chat.IsTemporary))
            .ExecuteDeleteAsync(cancellationToken);
    }
}