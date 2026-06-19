using Chat.Application.Chats.Queries.GetChats;
using Chat.Domain.Shared;

namespace Chat.Application.Tests.Chats;

internal sealed class FakeChatListReader(ChatListReadModel readModel) : IChatListReader
{
    public UserId? RequestedUserId { get; private set; }

    public int? RequestedLimit { get; private set; }

    public int? RequestedOffset { get; private set; }

    public int GetCallCount { get; private set; }

    public Task<ChatListReadModel> GetAsync
    (
        UserId userId,
        int limit,
        int offset,
        CancellationToken cancellationToken
    )
    {
        RequestedUserId = userId;
        RequestedLimit = limit;
        RequestedOffset = offset;
        GetCallCount++;

        return Task.FromResult(readModel);
    }
}