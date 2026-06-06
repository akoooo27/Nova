namespace Chat.Application.FavoriteModels.Results;

public sealed record FavoriteModelResult
(
    Guid Id,
    string UserId,
    Guid LlmModelId,
    DateTimeOffset CreatedAt
);