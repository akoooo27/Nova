using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Infrastructure.Database;

using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.ModelCatalog.Repositories;

internal sealed class LlmProviderRepository(ChatDbContext db) : ILlmProviderRepository
{
    public async Task<LlmProvider?> GetByIdAsync(LlmProviderId id, CancellationToken cancellationToken = default)
    {
        return await db.LlmProviders
            .Include(x => x.Models)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<LlmProvider?> GetByModelIdAsync(LlmModelId id, CancellationToken cancellationToken = default)
    {
        return await db.LlmProviders
            .Include(x => x.Models)
            .FirstOrDefaultAsync(x => x.Models.Any(y => y.Id == id), cancellationToken);
    }

    public async Task<bool> ExistsBySlugAsync(ProviderSlug slug, CancellationToken cancellationToken = default)
    {
        return await db.LlmProviders
            .AnyAsync(x => x.Slug == slug, cancellationToken);
    }

    public async Task<bool> ExistsBySlugAsync
    (
        ProviderSlug slug,
        LlmProviderId excludedProviderId,
        CancellationToken cancellationToken = default
    )
    {
        return await db.LlmProviders
            .AnyAsync
            (
                provider => provider.Slug == slug && provider.Id != excludedProviderId,
                cancellationToken
            );
    }

    public void Add(LlmProvider provider)
    {
        db.LlmProviders.Add(provider);
    }

    public void Remove(LlmProvider provider)
    {
        db.LlmProviders.Remove(provider);
    }
}