using FluentValidation;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.EnableLlmProvider;

internal sealed class EnableLlmProviderCommandValidator : AbstractValidator<EnableLlmProviderCommand>
{
    public EnableLlmProviderCommandValidator()
    {
        RuleFor(x => x.ProviderId)
            .NotEmpty();
    }
}