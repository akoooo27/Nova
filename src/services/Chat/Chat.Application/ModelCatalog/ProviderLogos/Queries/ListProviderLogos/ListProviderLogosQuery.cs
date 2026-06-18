using Chat.Application.Abstractions.ProviderLogos.Results;

using Mediator;

namespace Chat.Application.ModelCatalog.ProviderLogos.Queries.ListProviderLogos;

public sealed record ListProviderLogosQuery : IQuery<IReadOnlyCollection<ProviderLogoObject>>;