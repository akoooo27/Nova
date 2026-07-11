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

    /// <summary>
    /// Loads a chat with its messages by id WITHOUT owner scoping, no-tracking. Used only by the
    /// remix flow, authorized by the source share's <c>allow_remix</c> consent. The returned
    /// aggregate is a detached snapshot that must not be mutated or persisted.
    /// </summary>
    Task<ChatThread?> GetSnapshotByChatIdAsync
    (
        ChatId id,
        CancellationToken cancellationToken = default
    );

    void Add(ChatThread chat);

    Task<int> DeleteExpiredTemporaryChatsAsync
    (
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Hard-deletes an owner-scoped, non-temporary chat. Returns the number of
    /// affected rows (0 means not found, not owned, or temporary).
    /// </summary>
    Task<int> DeleteByIdAsync
    (
        ChatId id,
        UserId userId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Archives every non-temporary, not-yet-archived chat owned by the user.
    /// Sets only the archived flag; UpdatedAt is intentionally untouched so
    /// list ordering matches single-chat archive behavior.
    /// </summary>
    Task<int> ArchiveAllAsync
    (
        UserId userId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Hard-deletes every chat owned by the user. Temporary chats are excluded
    /// unless <paramref name="includeTemporary"/> is set (account-deletion purge).
    /// </summary>
    Task<int> DeleteAllAsync
    (
        UserId userId,
        bool includeTemporary = false,
        CancellationToken cancellationToken = default
    );
}