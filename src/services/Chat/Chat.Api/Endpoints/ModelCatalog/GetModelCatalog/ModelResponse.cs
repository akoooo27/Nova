namespace Chat.Api.Endpoints.ModelCatalog.GetModelCatalog;

internal sealed class ModelResponse
{
    public required Guid Id { get; init; }

    public required Guid ProviderId { get; init; }

    public required string ExternalModelId { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required int ContextWindow { get; init; }

    public required bool SupportsVision { get; init; }

    public required bool SupportsReasoning { get; init; }

    public required bool SupportsToolCalling { get; init; }

    public required int SortOrder { get; init; }
}