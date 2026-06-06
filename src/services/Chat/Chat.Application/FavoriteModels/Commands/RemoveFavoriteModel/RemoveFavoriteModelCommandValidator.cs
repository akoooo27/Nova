using FluentValidation;

namespace Chat.Application.FavoriteModels.Commands.RemoveFavoriteModel;

internal sealed class RemoveFavoriteModelCommandValidator : AbstractValidator<RemoveFavoriteModelCommand>
{
    public RemoveFavoriteModelCommandValidator()
    {
        RuleFor(x => x.LlmModelId)
            .NotEmpty();
    }
}