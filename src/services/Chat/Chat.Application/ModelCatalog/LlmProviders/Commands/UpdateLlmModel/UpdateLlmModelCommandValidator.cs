using FluentValidation;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.UpdateLlmModel;

internal sealed class UpdateLlmModelCommandValidator : AbstractValidator<UpdateLlmModelCommand>
{
    public UpdateLlmModelCommandValidator()
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