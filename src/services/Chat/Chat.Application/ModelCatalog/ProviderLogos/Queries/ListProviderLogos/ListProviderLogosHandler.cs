using Chat.Application.Abstractions.ProviderLogos;
using Chat.Application.Abstractions.ProviderLogos.Results;

using Mediator;

namespace Chat.Application.ModelCatalog.ProviderLogos.Queries.ListProviderLogos;

internal sealed class ListProviderLogosHandler(IProviderLogoStorage storage)
    : IQueryHandler<ListProviderLogosQuery, IReadOnlyCollection<ProviderLogoObject>>
{
    public async ValueTask<IReadOnlyCollection<ProviderLogoObject>> Handle
    (
        ListProviderLogosQuery query,
        CancellationToken cancellationToken
    ) => await storage.ListAsync(cancellationToken);
}