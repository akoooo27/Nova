using ErrorOr;

using Mediator;

namespace Chat.Application.FavoriteModels.Queries;

public sealed record GetFavoriteModelsQuery : IQuery<ErrorOr<FavoriteModelsReadModel>>;
