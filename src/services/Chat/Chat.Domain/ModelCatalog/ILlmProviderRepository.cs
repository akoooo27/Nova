using Chat.Domain.ModelCatalog.ValueObjects;

namespace Chat.Domain.ModelCatalog;

public interface ILlmProviderRepository
{
    Task<LlmProvider?> GetByIdAsync(LlmProviderId id, CancellationToken cancellationToken = default);

    Task<bool> ExistsBySlugAsync(ProviderSlug slug, CancellationToken cancellationToken = default);

    void Add(LlmProvider provider);
}