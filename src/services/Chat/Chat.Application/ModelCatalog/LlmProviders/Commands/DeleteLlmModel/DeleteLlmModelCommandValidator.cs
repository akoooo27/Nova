using FluentValidation;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.DeleteLlmModel;

internal sealed class DeleteLlmModelCommandValidator : AbstractValidator<DeleteLlmModelCommand>
{
    public DeleteLlmModelCommandValidator()
    {
        RuleFor(x => x.ProviderId)
            .NotEmpty();

        RuleFor(x => x.ModelId)
            .NotEmpty();
    }
}