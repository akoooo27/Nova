using FluentValidation;

namespace Chat.Application.FavoriteModels.Commands.AddFavoriteModel;

internal sealed class AddFavoriteModelCommandValidator : AbstractValidator<AddFavoriteModelCommand>
{
    public AddFavoriteModelCommandValidator()
    {
        RuleFor(x => x.LlmModelId)
            .NotEmpty();
    }
}