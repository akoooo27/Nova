namespace Chat.Application.FavoriteModels.Queries.GetFavoriteModels;

public sealed record FavoriteModelProviderReadModel
(
    Guid Id,
    string Name,
    string Slug,
    string? LogoKey
);