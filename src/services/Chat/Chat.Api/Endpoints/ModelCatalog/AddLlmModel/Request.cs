namespace Chat.Api.Endpoints.ModelCatalog.AddLlmModel;

internal sealed class Request
{
    public required string ExternalModelId { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required int ContextWindow { get; init; }

    public required bool SupportsVision { get; init; }

    public required bool SupportsReasoning { get; init; }

    public required bool SupportsToolCalling { get; init; }
}