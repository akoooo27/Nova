using Chat.Domain.Shared;

namespace Chat.Application.Chats.Queries.SearchChats;

public interface IChatSearchReader
{
    Task<ChatSearchReadModel> SearchAsync
    (
        UserId userId,
        string query,
        bool isArchived,
        int limit,
        int offset,
        CancellationToken cancellationToken
    );
}