using Chat.Domain.ModelCatalog.Events;

using Mediator;

using Shared.Infrastructure.DomainEvents;

using ZiggyCreatures.Caching.Fusion;

namespace Chat.Infrastructure.ModelCatalog.Caching;

internal sealed class LlmProviderDeletedCacheHandler(IFusionCache cache)
    : INotificationHandler<DomainEventNotification<LlmProviderDeleted>>
{
    public async ValueTask Handle
    (
        DomainEventNotification<LlmProviderDeleted> notification,
        CancellationToken cancellationToken
    )
    {
        await cache.RemoveByTagAsync(ModelCatalogCacheTags.Catalog, token: cancellationToken);
    }
}