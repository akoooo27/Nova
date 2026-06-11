using Chat.Domain.ModelCatalog.Events;

using Mediator;

using Shared.Infrastructure.DomainEvents;

using ZiggyCreatures.Caching.Fusion;

namespace Chat.Infrastructure.ModelCatalog.Caching;

internal sealed class LlmModelAvailabilityChangedCacheHandler(IFusionCache cache)
    : INotificationHandler<DomainEventNotification<LlmModelAvailabilityChanged>>
{
    public async ValueTask Handle
    (
        DomainEventNotification<LlmModelAvailabilityChanged> notification,
        CancellationToken cancellationToken
    )
    {
        await cache.RemoveByTagAsync(ModelCatalogCacheTags.Catalog, token: cancellationToken);
    }
}