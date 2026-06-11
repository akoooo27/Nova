namespace Chat.Api.Endpoints.ModelCatalog.Responses;

internal sealed class LlmProviderResponse
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Slug { get; init; }

    public required bool IsFeatured { get; init; }

    public required bool IsEnabled { get; init; }

    public string? LogoKey { get; init; }

    public required IReadOnlyCollection<LlmModelResponse> Models { get; init; }
}