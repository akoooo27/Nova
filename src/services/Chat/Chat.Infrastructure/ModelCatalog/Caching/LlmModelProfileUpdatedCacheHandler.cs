using Chat.Domain.ModelCatalog.Events;

using Mediator;

using Shared.Infrastructure.DomainEvents;

using ZiggyCreatures.Caching.Fusion;

namespace Chat.Infrastructure.ModelCatalog.Caching;

internal sealed class LlmModelProfileUpdatedCacheHandler(IFusionCache cache)
    : INotificationHandler<DomainEventNotification<LlmModelProfileUpdated>>
{
    public async ValueTask Handle
    (
        DomainEventNotification<LlmModelProfileUpdated> notification,
        CancellationToken cancellationToken
    )
    {
        await cache.RemoveByTagAsync(ModelCatalogCacheTags.Catalog, token: cancellationToken);
    }
}