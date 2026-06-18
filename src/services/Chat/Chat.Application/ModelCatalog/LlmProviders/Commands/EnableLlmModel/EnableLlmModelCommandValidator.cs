using FluentValidation;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.EnableLlmModel;

internal sealed class EnableLlmModelCommandValidator : AbstractValidator<EnableLlmModelCommand>
{
    public EnableLlmModelCommandValidator()
    {
        RuleFor(x => x.ProviderId)
            .NotEmpty();

        RuleFor(x => x.ModelId)
            .NotEmpty();
    }
}