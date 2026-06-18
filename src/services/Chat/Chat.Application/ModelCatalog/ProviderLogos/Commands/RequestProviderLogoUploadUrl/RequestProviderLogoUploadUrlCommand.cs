using Chat.Application.Abstractions.ProviderLogos.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.ProviderLogos.Commands.RequestProviderLogoUploadUrl;

public sealed record RequestProviderLogoUploadUrlCommand
(
    Guid ProviderId,
    string ContentType
) : ICommand<ErrorOr<ProviderLogoUploadUrl>>;