using Chat.Domain.Chats.ValueObjects;

namespace Chat.Domain.Chats;

public interface IChatRepository
{
    Task<ChatThread?> GetByIdAsync(ChatId id, CancellationToken cancellationToken = default);

    void Add(ChatThread chat);
}