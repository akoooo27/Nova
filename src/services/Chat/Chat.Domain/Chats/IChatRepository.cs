using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Domain.Chats;

public interface IChatRepository
{
    Task<ChatThread?> GetByIdAsync
    (
        ChatId id,
        UserId userId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Loads an owner-scoped chat with its messages without change tracking. Intended for
    /// reading a branch source: the returned aggregate is a detached snapshot that must not
    /// be mutated or persisted, so cloning it never re-saves the source.
    /// </summary>
    Task<ChatThread?> GetSnapshotByIdAsync
    (
        ChatId id,
        UserId userId,
        CancellationToken cancellationToken = default
    );

    void Add(ChatThread chat);

    Task<int> DeleteExpiredTemporaryChatsAsync
    (
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default
    );
}