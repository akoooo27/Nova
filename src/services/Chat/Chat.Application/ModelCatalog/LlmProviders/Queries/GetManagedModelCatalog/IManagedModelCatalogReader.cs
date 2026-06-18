namespace Chat.Application.ModelCatalog.LlmProviders.Queries.GetManagedModelCatalog;

public interface IManagedModelCatalogReader
{
    Task<ManagedModelCatalogReadModel> GetAsync(CancellationToken cancellationToken);
}