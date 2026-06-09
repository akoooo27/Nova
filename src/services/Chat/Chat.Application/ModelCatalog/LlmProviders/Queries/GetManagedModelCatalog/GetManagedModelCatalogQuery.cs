using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Queries.GetManagedModelCatalog;

public sealed record GetManagedModelCatalogQuery : IQuery<ManagedModelCatalogReadModel>;