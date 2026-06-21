using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;
using Chat.Domain.SharedChats.ValueObjects;

namespace Chat.Domain.SharedChats;

public interface ISharedChatRepository
{
    Task<SharedChat?> GetBySourceAsync
    (
        UserId userId,
        ChatId chatId,
        ChatMessageId currentNodeId,
        CancellationToken cancellationToken = default
    );

    Task<bool> TryAddAsync(SharedChat sharedChat, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync
    (
        SharedChatId id,
        UserId userId,
        CancellationToken cancellationToken = default
    );

    Task<int> DeleteAllAsync(UserId userId, CancellationToken cancellationToken = default);
}