using Chat.Application.Abstractions.ModelCatalog;
using Chat.Application.ModelCatalog.LlmProviders.Queries.GetPublicModelCatalog;
using Chat.Infrastructure.ModelCatalog.Readers;

using ZiggyCreatures.Caching.Fusion;

namespace Chat.Infrastructure.ModelCatalog.Caching;

internal sealed class CachedPublicModelCatalogReader(IFusionCache cache, PublicModelCatalogDapperReader inner)
    : IPublicModelCatalogReader
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public async Task<PublicModelCatalogReadModel> GetAsync(CancellationToken cancellationToken)
    {
        return await cache.GetOrSetAsync
        (
            key: ModelCatalogCacheKeys.PublicCatalog,
            factory: async ct => await inner.GetAsync(ct),
            options => options
                .SetDuration(CacheDuration),
            tags: [ModelCatalogCacheTags.Catalog],
            token: cancellationToken
        );
    }
}