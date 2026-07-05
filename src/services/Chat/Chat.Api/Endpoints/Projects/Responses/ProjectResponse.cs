using Chat.Application.Projects.Results;

namespace Chat.Api.Endpoints.Projects.Responses;

internal sealed class ProjectResponse
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string? Instructions { get; init; }

    public string? Emoji { get; init; }

    public string? Theme { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public static ProjectResponse From(ProjectResult result) => new()
    {
        Id = result.Id,
        Name = result.Name,
        Instructions = result.Instructions,
        Emoji = result.Emoji,
        Theme = result.Theme,
        CreatedAt = result.CreatedAt,
        UpdatedAt = result.UpdatedAt
    };
}