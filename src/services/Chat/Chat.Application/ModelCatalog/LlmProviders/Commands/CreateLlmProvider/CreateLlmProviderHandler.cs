using Chat.Application.Abstractions.Database;
using Chat.Application.ModelCatalog.LlmProviders.Errors;
using Chat.Application.ModelCatalog.LlmProviders.Results;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Microsoft.EntityFrameworkCore;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.CreateLlmProvider;

internal sealed class CreateLlmProviderHandler(IApplicationDbContext db)
    : ICommandHandler<CreateLlmProviderCommand, ErrorOr<LlmProviderResult>>
{
    public async ValueTask<ErrorOr<LlmProviderResult>> Handle(CreateLlmProviderCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<ProviderName> nameResult = ProviderName.Create(command.Name);
        ErrorOr<ProviderSlug> slugResult = ProviderSlug.Create(command.Slug);
        ErrorOr<SortOrder> sortOrderResult = command.SortOrder is not null
            ? SortOrder.Create(command.SortOrder.Value)
            : SortOrder.First;

        List<Error> errors = [];

        if (nameResult.IsError)
        {
            errors.AddRange(nameResult.Errors);
        }

        if (slugResult.IsError)
        {
            errors.AddRange(slugResult.Errors);
        }

        if (sortOrderResult.IsError)
        {
            errors.AddRange(sortOrderResult.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        ProviderSlug slug = slugResult.Value;

        bool slugExists = await db.LlmProviders
            .AnyAsync(x => x.Slug == slug, cancellationToken);

        if (slugExists)
        {
            return LlmProviderOperationFault.SlugAlreadyExists(slug);
        }

        LlmProvider provider = LlmProvider.Create
        (
            name: nameResult.Value,
            slug: slug,
            sortOrder: sortOrderResult.Value
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

        db.LlmProviders.Add(provider);
        await db.SaveChangesAsync(cancellationToken);

        return provider.ToResult();
    }
}