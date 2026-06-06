using Chat.Application.FavoriteModels.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.FavoriteModels.Commands.AddFavoriteModel;

public sealed record AddFavoriteModelCommand(Guid LlmModelId) : ICommand<ErrorOr<FavoriteModelResult>>;