using Chat.Application.ModelCatalog.LlmProviders.Queries.GetPublicModelCatalog;

namespace Chat.Application.Abstractions.ModelCatalog;

public interface IPublicModelCatalogReader
{
    Task<PublicModelCatalogReadModel> GetAsync(CancellationToken cancellationToken);
}
