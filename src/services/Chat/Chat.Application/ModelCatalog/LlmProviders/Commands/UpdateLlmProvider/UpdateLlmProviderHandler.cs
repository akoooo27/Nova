using Chat.Application.Abstractions.Database;
using Chat.Application.ModelCatalog.LlmProviders.Errors;
using Chat.Application.ModelCatalog.LlmProviders.Results;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.UpdateLlmProvider;

internal sealed class UpdateLlmProviderHandler
(
    ILlmProviderRepository providers,
    IUnitOfWork unitOfWork
) : ICommandHandler<UpdateLlmProviderCommand, ErrorOr<LlmProviderResult>>
{
    public async ValueTask<ErrorOr<LlmProviderResult>> Handle
    (
        UpdateLlmProviderCommand command,
        CancellationToken cancellationToken
    )
    {
        ErrorOr<LlmProviderId> providerIdResult = LlmProviderId.Create(command.ProviderId);
        ErrorOr<ProviderName> nameResult = ProviderName.Create(command.Name);
        ErrorOr<ProviderSlug> slugResult = ProviderSlug.Create(command.Slug);
        AssetKey? logoKey = null;
        List<Error> errors = [];

        if (providerIdResult.IsError)
        {
            errors.AddRange(providerIdResult.Errors);
        }

        if (nameResult.IsError)
        {
            errors.AddRange(nameResult.Errors);
        }

        if (slugResult.IsError)
        {
            errors.AddRange(slugResult.Errors);
        }

        if (command.LogoKey is not null)
        {
            ErrorOr<AssetKey> logoKeyResult = AssetKey.Create(command.LogoKey);

            if (logoKeyResult.IsError)
            {
                errors.AddRange(logoKeyResult.Errors);
            }
            else
            {
                logoKey = logoKeyResult.Value;
            }
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        LlmProvider? provider = await providers.GetByIdAsync(providerIdResult.Value, cancellationToken);

        if (provider is null)
        {
            return LlmProviderOperationErrors.ProviderNotFound(providerIdResult.Value);
        }

        ProviderSlug slug = slugResult.Value;

        bool slugExists = await providers.ExistsBySlugAsync
        (
            slug: slug,
            excludedProviderId: provider.Id,
            cancellationToken: cancellationToken
        );

        if (slugExists)
        {
            return LlmProviderOperationErrors.SlugAlreadyExists(slug);
        }

        provider.UpdateDetails
        (
            name: nameResult.Value,
            slug: slug,
            logoKey: logoKey,
            isFeatured: command.IsFeatured
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return provider.ToResult();
    }
}