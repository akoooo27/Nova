using FluentValidation;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.DeleteLlmProvider;

internal sealed class DeleteLlmProviderCommandValidator : AbstractValidator<DeleteLlmProviderCommand>
{
    public DeleteLlmProviderCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty();
    }
}