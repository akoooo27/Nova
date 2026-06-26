using Chat.Application.SharedChats.Queries.GetSharedChats;
using Chat.Domain.Shared;

namespace Chat.Application.Tests.SharedChats;

internal sealed class FakeSharedChatListReader(SharedChatListReadModel result) : ISharedChatListReader
{
    public UserId? UserId { get; private set; }

    public int? Limit { get; private set; }

    public int? Offset { get; private set; }

    public int CallCount { get; private set; }

    public Task<SharedChatListReadModel> GetAsync
    (
        UserId userId,
        int limit,
        int offset,
        CancellationToken cancellationToken
    )
    {
        UserId = userId;
        Limit = limit;
        Offset = offset;
        CallCount++;

        return Task.FromResult(result);
    }
}