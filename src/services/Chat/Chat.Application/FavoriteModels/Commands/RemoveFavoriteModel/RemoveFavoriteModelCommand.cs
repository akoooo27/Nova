using ErrorOr;

using Mediator;

namespace Chat.Application.FavoriteModels.Commands.RemoveFavoriteModel;

public sealed record RemoveFavoriteModelCommand(Guid LlmModelId) : ICommand<ErrorOr<Success>>;