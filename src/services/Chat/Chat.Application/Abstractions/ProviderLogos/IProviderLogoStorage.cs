using Chat.Application.Abstractions.ProviderLogos.Results;

using ErrorOr;

namespace Chat.Application.Abstractions.ProviderLogos;

public interface IProviderLogoStorage
{
    Task<ErrorOr<ProviderLogoUploadUrl>> CreateUploadUrlAsync
    (
        string providerSlug,
        string contentType,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyCollection<ProviderLogoObject>> ListAsync(CancellationToken cancellationToken);
}