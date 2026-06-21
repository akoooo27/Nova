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

    Task<bool> TryAddAsync(SharedChat sharedChat, CancellationToken cancellationToken = default);

    void Remove(SharedChat sharedChat);

    Task<int> DeleteAllAsync(UserId userId, CancellationToken cancellationToken = default);
}