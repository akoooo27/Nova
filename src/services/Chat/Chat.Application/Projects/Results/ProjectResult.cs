namespace Chat.Application.Projects.Results;

public sealed record ProjectResult
(
    Guid Id,
    string Name,
    string? Instructions,
    string? Emoji,
    string? Theme,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);