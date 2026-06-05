using Chat.Domain.ModelCatalog.Events;

using Mediator;

using Shared.Infrastructure.DomainEvents;

using ZiggyCreatures.Caching.Fusion;

namespace Chat.Infrastructure.ModelCatalog.Caching;

internal sealed class LlmProviderUpdatedCacheHandler(IFusionCache cache)
    : INotificationHandler<DomainEventNotification<LlmProviderUpdated>>
{
    public async ValueTask Handle
    (
        DomainEventNotification<LlmProviderUpdated> notification,
        CancellationToken cancellationToken
    )
    {
        await cache.RemoveByTagAsync(ModelCatalogCacheTags.Catalog, token: cancellationToken);
    }
}