using FluentValidation;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.UpdateLlmModelProfile;

internal sealed class UpdateLlmModelProfileCommandValidator : AbstractValidator<UpdateLlmModelProfileCommand>
{
    public UpdateLlmModelProfileCommandValidator()
    {
        RuleFor(x => x.ProviderId)
            .NotEmpty();

        RuleFor(x => x.ModelId)
            .NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(ModelCatalogLimits.ModelNameMaxLength);

        RuleFor(x => x.Description)
            .NotEmpty()
            .MaximumLength(ModelCatalogLimits.ModelDescriptionMaxLength);

        RuleFor(x => x.ContextWindow)
            .GreaterThanOrEqualTo(1);
    }
}