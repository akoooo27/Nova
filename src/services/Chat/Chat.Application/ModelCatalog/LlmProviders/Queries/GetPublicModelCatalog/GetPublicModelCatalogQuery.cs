using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Queries.GetPublicModelCatalog;

public sealed record GetPublicModelCatalogQuery : IQuery<PublicModelCatalogReadModel>;