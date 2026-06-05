using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.ValueObjects;

namespace Chat.Application.Tests.ModelCatalog.LlmProviders;

internal sealed class FakeLlmProviderRepository : ILlmProviderRepository
{
    private readonly List<LlmProvider> _providers = [];
    private readonly HashSet<ProviderSlug> _existingSlugs = [];

    public IReadOnlyCollection<LlmProvider> AddedProviders => _providers;

    public void AddExistingSlug(ProviderSlug slug)
    {
        _existingSlugs.Add(slug);
    }

    public void AddExistingProvider(LlmProvider provider)
    {
        _providers.Add(provider);
    }

    public Task<LlmProvider?> GetByIdAsync(LlmProviderId id, CancellationToken cancellationToken = default)
    {
        LlmProvider? provider = _providers.FirstOrDefault(x => x.Id == id);

        return Task.FromResult(provider);
    }

    public Task<bool> ExistsBySlugAsync(ProviderSlug slug, CancellationToken cancellationToken = default)
    {
        bool exists = _existingSlugs.Contains(slug) || _providers.Any(x => x.Slug == slug);

        return Task.FromResult(exists);
    }

    public Task<bool> ExistsBySlugAsync
    (
        ProviderSlug slug,
        LlmProviderId excludedProviderId,
        CancellationToken cancellationToken = default
    )
    {
        bool exists = _existingSlugs.Contains(slug)
                      || _providers.Any(provider => provider.Id != excludedProviderId && provider.Slug == slug);

        return Task.FromResult(exists);
    }

    public void Add(LlmProvider provider)
    {
        _providers.Add(provider);
    }
}