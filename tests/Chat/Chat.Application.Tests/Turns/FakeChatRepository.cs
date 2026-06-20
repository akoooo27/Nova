using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Application.Tests.Turns;

internal sealed class FakeChatRepository : IChatRepository
{
    private readonly List<ChatThread> _threads = [];
    private readonly List<ChatThread> _addedThreads = [];

    public IReadOnlyList<ChatThread> Threads => _threads;

    public IReadOnlyList<ChatThread> AddedThreads => _addedThreads;

    public int SnapshotGetCallCount { get; private set; }

    public void Seed(ChatThread thread)
    {
        _threads.Add(thread);
    }

    public Task<ChatThread?> GetByIdAsync
    (
        ChatId id,
        UserId userId,
        CancellationToken cancellationToken = default
    )
    {
        ChatThread? thread = _threads.FirstOrDefault(x => x.Id == id && x.UserId == userId);

        return Task.FromResult(thread);
    }

    public Task<ChatThread?> GetSnapshotByIdAsync
    (
        ChatId id,
        UserId userId,
        CancellationToken cancellationToken = default
    )
    {
        SnapshotGetCallCount++;
        ChatThread? thread = _threads.FirstOrDefault(x => x.Id == id && x.UserId == userId);

        return Task.FromResult(thread);
    }

    public void Add(ChatThread chat)
    {
        _threads.Add(chat);
        _addedThreads.Add(chat);
    }

    public Task<int> DeleteExpiredTemporaryChatsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        int removed = _threads.RemoveAll(thread => thread.IsTemporary && thread.UpdatedAt < olderThan);

        return Task.FromResult(removed);
    }
}