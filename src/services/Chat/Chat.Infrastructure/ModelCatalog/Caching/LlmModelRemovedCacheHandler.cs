using Chat.Domain.ModelCatalog.Events;

using Mediator;

using Shared.Infrastructure.DomainEvents;

using ZiggyCreatures.Caching.Fusion;

namespace Chat.Infrastructure.ModelCatalog.Caching;

internal sealed class LlmModelRemovedCacheHandler(IFusionCache cache)
    : INotificationHandler<DomainEventNotification<LlmModelRemoved>>
{
    public async ValueTask Handle
    (
        DomainEventNotification<LlmModelRemoved> notification,
        CancellationToken cancellationToken
    )
    {
        await cache.RemoveByTagAsync(ModelCatalogCacheTags.Catalog, token: cancellationToken);
    }
}