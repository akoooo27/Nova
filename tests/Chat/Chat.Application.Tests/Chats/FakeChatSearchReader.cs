using Chat.Application.Chats.Queries.SearchChats;
using Chat.Domain.Shared;

namespace Chat.Application.Tests.Chats;

internal sealed class FakeChatSearchReader(ChatSearchReadModel readModel) : IChatSearchReader
{
    public UserId? RequestedUserId { get; private set; }

    public string? RequestedQuery { get; private set; }

    public bool? RequestedIsArchived { get; private set; }

    public int? RequestedLimit { get; private set; }

    public int? RequestedOffset { get; private set; }

    public int SearchCallCount { get; private set; }

    public Task<ChatSearchReadModel> SearchAsync
    (
        UserId userId,
        string query,
        bool isArchived,
        int limit,
        int offset,
        CancellationToken cancellationToken
    )
    {
        RequestedUserId = userId;
        RequestedQuery = query;
        RequestedIsArchived = isArchived;
        RequestedLimit = limit;
        RequestedOffset = offset;
        SearchCallCount++;

        return Task.FromResult(readModel);
    }
}