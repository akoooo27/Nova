using FluentValidation;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.DisableLlmProvider;

internal sealed class DisableLlmProviderCommandValidator : AbstractValidator<DisableLlmProviderCommand>
{
    public DisableLlmProviderCommandValidator()
    {
        RuleFor(x => x.ProviderId)
            .NotEmpty();
    }
}