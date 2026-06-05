using FluentValidation;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.AddLlmModel;

internal sealed class AddLlmModelCommandValidator : AbstractValidator<AddLlmModelCommand>
{
    public AddLlmModelCommandValidator()
    {
        RuleFor(x => x.ProviderId)
            .NotEmpty();

        RuleFor(x => x.ExternalModelId)
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