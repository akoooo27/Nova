using Chat.Domain.Shared;

namespace Chat.Application.SharedChats.Queries.GetSharedChats;

public interface ISharedChatListReader
{
    Task<SharedChatListReadModel> GetAsync
    (
        UserId userId,
        int limit,
        int offset,
        CancellationToken cancellationToken
    );
}