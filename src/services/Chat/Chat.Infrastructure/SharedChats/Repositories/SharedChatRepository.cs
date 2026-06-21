using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;
using Chat.Domain.SharedChats;
using Chat.Domain.SharedChats.ValueObjects;
using Chat.Infrastructure.Database;

using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.SharedChats.Repositories;

internal sealed class SharedChatRepository(ChatDbContext db) : ISharedChatRepository
{
    public async Task<SharedChat?> GetByIdAsync
    (
        SharedChatId id,
        UserId userId,
        CancellationToken cancellationToken = default
    )
    {
        return await db.SharedChats
            .FirstOrDefaultAsync
            (
                x => x.Id == id && x.UserId == userId,
                cancellationToken
            );
    }

    public async Task<SharedChat?> GetBySourceAsync
    (
        UserId userId,
        ChatId chatId,
        ChatMessageId currentNodeId,
        CancellationToken cancellationToken = default
    )
    {
        return await db.SharedChats
            .AsNoTracking()
            .FirstOrDefaultAsync
            (
                x => x.UserId == userId
                     && x.ChatId == chatId
                     && x.CurrentMessageId == currentNodeId,
                cancellationToken
            );
    }

    public async Task<bool> TryAddAsync(SharedChat sharedChat, CancellationToken cancellationToken = default)
    {
        int affected = await db.Database.ExecuteSqlAsync
        (
            $"""
             insert into shared_chats
                 (id, user_id, chat_id, current_message_id, title, created_at)
             values
                 ({sharedChat.Id.Value}, {sharedChat.UserId.Value}, {sharedChat.ChatId.Value},
                  {sharedChat.CurrentMessageId.Value}, {sharedChat.Title.Value}, {sharedChat.CreatedAt})
             on conflict (chat_id, current_message_id) do nothing;
             """,
            cancellationToken
        );

        return affected == 1;
    }

    public void Remove(SharedChat sharedChat)
    {
        db.SharedChats.Remove(sharedChat);
    }

    public async Task<int> DeleteAllAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        return await db.SharedChats
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}