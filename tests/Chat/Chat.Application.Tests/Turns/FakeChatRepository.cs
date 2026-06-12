using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Application.Tests.Turns;

internal sealed class FakeChatRepository : IChatRepository
{
    private readonly List<ChatThread> _threads = [];

    public IReadOnlyList<ChatThread> Threads => _threads;

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

    public void Add(ChatThread chat)
    {
        _threads.Add(chat);
    }
}