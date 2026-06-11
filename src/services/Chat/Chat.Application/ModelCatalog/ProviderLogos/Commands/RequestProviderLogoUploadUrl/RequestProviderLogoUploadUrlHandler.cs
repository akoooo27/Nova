using Chat.Application.Abstractions.ProviderLogos;
using Chat.Application.Abstractions.ProviderLogos.Results;
using Chat.Application.ModelCatalog.LlmProviders.Errors;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.ProviderLogos.Commands.RequestProviderLogoUploadUrl;

internal sealed class RequestProviderLogoUploadUrlHandler
(
    ILlmProviderRepository providers,
    IProviderLogoStorage storage
) : ICommandHandler<RequestProviderLogoUploadUrlCommand, ErrorOr<ProviderLogoUploadUrl>>
{
    public async ValueTask<ErrorOr<ProviderLogoUploadUrl>> Handle
    (
        RequestProviderLogoUploadUrlCommand command,
        CancellationToken cancellationToken
    )
    {
        ErrorOr<LlmProviderId> providerIdResult = LlmProviderId.Create(command.ProviderId);

        if (providerIdResult.IsError)
        {
            return providerIdResult.Errors;
        }

        LlmProvider? provider = await providers.GetByIdAsync(providerIdResult.Value, cancellationToken);

        if (provider is null)
        {
            return LlmProviderOperationErrors.ProviderNotFound(providerIdResult.Value);
        }

        return await storage.CreateUploadUrlAsync
        (
            providerSlug: provider.Slug.Value,
            contentType: command.ContentType,
            cancellationToken: cancellationToken
        );
    }
}