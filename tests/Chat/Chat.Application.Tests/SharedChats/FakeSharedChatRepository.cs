using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;
using Chat.Domain.SharedChats;
using Chat.Domain.SharedChats.ValueObjects;

namespace Chat.Application.Tests.SharedChats;

internal sealed class FakeSharedChatRepository : ISharedChatRepository
{
    private readonly List<SharedChat> _items = [];

    public IReadOnlyList<SharedChat> Items => _items;

    public int TryAddCallCount { get; private set; }

    public void Seed(SharedChat sharedChat)
    {
        _items.Add(sharedChat);
    }

    public Task<SharedChat?> GetByIdAsync
    (
        SharedChatId id,
        UserId userId,
        CancellationToken cancellationToken = default
    )
    {
        SharedChat? match = _items.FirstOrDefault(x => x.Id == id && x.UserId == userId);

        return Task.FromResult(match);
    }

    public Task<SharedChat?> GetBySourceAsync
    (
        UserId userId,
        ChatId chatId,
        ChatMessageId currentNodeId,
        CancellationToken cancellationToken = default
    )
    {
        SharedChat? match = _items.FirstOrDefault
        (
            x => x.UserId == userId
                 && x.ChatId == chatId
                 && x.CurrentMessageId == currentNodeId
        );

        return Task.FromResult(match);
    }

    public Task<bool> TryAddAsync(SharedChat sharedChat, CancellationToken cancellationToken = default)
    {
        TryAddCallCount++;

        bool pairExists = _items.Any
        (
            x => x.ChatId == sharedChat.ChatId
                 && x.CurrentMessageId == sharedChat.CurrentMessageId
        );

        if (pairExists)
        {
            return Task.FromResult(false);
        }

        _items.Add(sharedChat);

        return Task.FromResult(true);
    }

    public void Remove(SharedChat sharedChat)
    {
        _items.RemoveAll(x => x.Id == sharedChat.Id);
    }

    public Task<int> DeleteAllAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        int removed = _items.RemoveAll(x => x.UserId == userId);

        return Task.FromResult(removed);
    }

    public Task<SharedChat?> GetForRemixAsync(SharedChatId id, CancellationToken cancellationToken = default)
    {
        SharedChat? match = _items.FirstOrDefault(x => x.Id == id);

        return Task.FromResult(match);
    }
}