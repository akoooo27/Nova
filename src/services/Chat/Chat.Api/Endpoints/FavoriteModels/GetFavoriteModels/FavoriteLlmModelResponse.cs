namespace Chat.Api.Endpoints.FavoriteModels.GetFavoriteModels;

internal sealed class FavoriteLlmModelResponse
{
    public required Guid Id { get; init; }

    public required string ExternalModelId { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required int ContextWindow { get; init; }

    public required bool SupportsVision { get; init; }

    public required bool SupportsReasoning { get; init; }

    public required bool SupportsToolCalling { get; init; }

    public required bool IsEnabled { get; init; }

    public required ProviderResponse Provider { get; init; }
}