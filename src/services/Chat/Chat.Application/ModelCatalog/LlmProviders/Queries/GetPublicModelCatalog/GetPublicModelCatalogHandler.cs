using Chat.Application.Abstractions.ModelCatalog;

using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Queries.GetPublicModelCatalog;

internal sealed class GetPublicModelCatalogHandler(IPublicModelCatalogReader reader)
    : IQueryHandler<GetPublicModelCatalogQuery, PublicModelCatalogReadModel>
{
    public async ValueTask<PublicModelCatalogReadModel> Handle(GetPublicModelCatalogQuery query, CancellationToken cancellationToken)
    {
        return await reader.GetAsync(cancellationToken);
    }
}