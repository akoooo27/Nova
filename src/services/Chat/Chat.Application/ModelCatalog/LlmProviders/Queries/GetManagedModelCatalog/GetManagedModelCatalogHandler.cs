using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Queries.GetManagedModelCatalog;

internal sealed class GetManagedModelCatalogHandler(IManagedModelCatalogReader reader)
    : IQueryHandler<GetManagedModelCatalogQuery, ManagedModelCatalogReadModel>
{
    public async ValueTask<ManagedModelCatalogReadModel> Handle(GetManagedModelCatalogQuery query,
        CancellationToken cancellationToken)
        => await reader.GetAsync(cancellationToken);
}