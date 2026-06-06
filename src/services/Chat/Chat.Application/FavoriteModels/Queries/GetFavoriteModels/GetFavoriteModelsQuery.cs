using ErrorOr;

using Mediator;

namespace Chat.Application.FavoriteModels.Queries.GetFavoriteModels;

public sealed record GetFavoriteModelsQuery : IQuery<ErrorOr<FavoriteModelsReadModel>>;