namespace Chat.Api.Endpoints.FavoriteModels.GetFavoriteModels;

internal sealed class ProviderResponse
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Slug { get; init; }

    public string? LogoKey { get; init; }
}