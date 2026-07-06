namespace Chat.Application.Projects.Queries.ListProjects;

public sealed record ProjectSummaryReadModel
(
    Guid Id,
    string Name,
    string? Instructions,
    string? Emoji,
    string? Theme,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);