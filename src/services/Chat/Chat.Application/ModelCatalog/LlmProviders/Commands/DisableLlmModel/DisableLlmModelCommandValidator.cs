using FluentValidation;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.DisableLlmModel;

internal sealed class DisableLlmModelCommandValidator : AbstractValidator<DisableLlmModelCommand>
{
    public DisableLlmModelCommandValidator()
    {
        RuleFor(x => x.ProviderId)
            .NotEmpty();

        RuleFor(x => x.ModelId)
            .NotEmpty();
    }
}