using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Application.Chats.Queries.GetChat;

public interface IChatDetailReader
{
    Task<ChatDetailReadModel?> GetAsync
    (
        ChatId chatId,
        UserId userId,
        CancellationToken cancellationToken
    );
}