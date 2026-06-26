using Chat.Domain.SharedChats.ValueObjects;

namespace Chat.Application.SharedChats.Queries.GetPublicSharedChat;

public interface IPublicSharedChatReader
{
    Task<PublicSharedChatReadModel?> GetAsync(SharedChatId id, CancellationToken cancellationToken);
}