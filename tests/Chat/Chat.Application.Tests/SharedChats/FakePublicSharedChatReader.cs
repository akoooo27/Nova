using Chat.Application.SharedChats.Queries.GetPublicSharedChat;
using Chat.Domain.SharedChats.ValueObjects;

namespace Chat.Application.Tests.SharedChats;

internal sealed class FakePublicSharedChatReader(PublicSharedChatReadModel? result) : IPublicSharedChatReader
{
    public SharedChatId? SharedChatId { get; private set; }

    public int CallCount { get; private set; }

    public Task<PublicSharedChatReadModel?> GetAsync
    (
        SharedChatId id,
        CancellationToken cancellationToken
    )
    {
        SharedChatId = id;
        CallCount++;

        return Task.FromResult(result);
    }
}