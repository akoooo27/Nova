using Chat.Domain.Shared;

namespace Chat.Application.Chats.Queries.GetChats;

public interface IChatListReader
{
    Task<ChatListReadModel> GetAsync
    (
        UserId userId,
        int limit,
        int offset,
        CancellationToken cancellationToken
    );
}