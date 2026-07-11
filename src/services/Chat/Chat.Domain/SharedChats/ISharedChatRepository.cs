using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;
using Chat.Domain.SharedChats.ValueObjects;

namespace Chat.Domain.SharedChats;

public interface ISharedChatRepository
{
    Task<SharedChat?> GetByIdAsync
    (
        SharedChatId id,
        UserId userId,
        CancellationToken cancellationToken = default
    );

    Task<SharedChat?> GetBySourceAsync
    (
        UserId userId,
        ChatId chatId,
        ChatMessageId currentNodeId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Loads a shared chat by id WITHOUT owner scoping, no-tracking. Used by the remix flow to read
    /// the sharer's consent flag and source pointer for any authenticated viewer.
    /// </summary>
    Task<SharedChat?> GetForRemixAsync
    (
        SharedChatId id,
        CancellationToken cancellationToken = default
    );

    Task<bool> TryAddAsync(SharedChat sharedChat, CancellationToken cancellationToken = default);

    void Remove(SharedChat sharedChat);

    Task<int> DeleteAllAsync(UserId userId, CancellationToken cancellationToken = default);
}