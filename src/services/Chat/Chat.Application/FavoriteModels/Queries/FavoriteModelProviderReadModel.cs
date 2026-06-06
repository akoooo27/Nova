namespace Chat.Application.FavoriteModels.Queries;

public sealed record FavoriteModelProviderReadModel
(
    Guid Id,
    string Name,
    string Slug,
    string? LogoKey
);