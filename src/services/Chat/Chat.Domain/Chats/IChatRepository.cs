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

    void Add(ChatThread chat);
}