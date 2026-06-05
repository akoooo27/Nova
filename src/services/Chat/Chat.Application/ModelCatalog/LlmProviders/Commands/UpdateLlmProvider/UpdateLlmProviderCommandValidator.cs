using FluentValidation;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.UpdateLlmProvider;

internal sealed class UpdateLlmProviderCommandValidator : AbstractValidator<UpdateLlmProviderCommand>
{
    public UpdateLlmProviderCommandValidator()
    {
        RuleFor(x => x.ProviderId)
            .NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(ModelCatalogLimits.ProviderNameMaxLength);

        RuleFor(x => x.Slug)
            .NotEmpty()
            .MaximumLength(ModelCatalogLimits.ProviderSlugMaxLength);

        RuleFor(x => x.LogoKey)
            .MaximumLength(ModelCatalogLimits.ProviderLogoKeyMaxLength)
            .When(x => x.LogoKey is not null);
    }
}