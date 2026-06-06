using Chat.Application.Abstractions.Database;
using Chat.Application.ModelCatalog.LlmProviders.Errors;
using Chat.Application.ModelCatalog.LlmProviders.Results;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.CreateLlmProvider;

internal sealed class CreateLlmProviderHandler(ILlmProviderRepository providers, IUnitOfWork unitOfWork)
    : ICommandHandler<CreateLlmProviderCommand, ErrorOr<LlmProviderResult>>
{
    public async ValueTask<ErrorOr<LlmProviderResult>> Handle(CreateLlmProviderCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<ProviderName> nameResult = ProviderName.Create(command.Name);
        ErrorOr<ProviderSlug> slugResult = ProviderSlug.Create(command.Slug);

        List<Error> errors = [];

        if (nameResult.IsError)
        {
            errors.AddRange(nameResult.Errors);
        }

        if (slugResult.IsError)
        {
            errors.AddRange(slugResult.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        ProviderSlug slug = slugResult.Value;

        bool slugExists = await providers.ExistsBySlugAsync(slug, cancellationToken);

        if (slugExists)
        {
            return LlmProviderOperationErrors.SlugAlreadyExists(slug);
        }

        LlmProvider provider = LlmProvider.Create
        (
            name: nameResult.Value,
            slug: slug,
            isFeatured: command.IsFeatured
        );

        if (command.LogoKey is not null)
        {
            ErrorOr<AssetKey> logoKeyResult = AssetKey.Create(command.LogoKey);

            if (logoKeyResult.IsError)
            {
                return logoKeyResult.Errors;
            }

            provider.UpdateLogoKey(logoKeyResult.Value);
        }

        providers.Add(provider);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return provider.ToResult();
    }
}