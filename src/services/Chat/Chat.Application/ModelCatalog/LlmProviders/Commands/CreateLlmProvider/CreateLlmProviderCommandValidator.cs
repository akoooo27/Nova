using FluentValidation;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.CreateLlmProvider;

internal sealed class CreateLlmProviderCommandValidator : AbstractValidator<CreateLlmProviderCommand>
{
    public CreateLlmProviderCommandValidator()
    {
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