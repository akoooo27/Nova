namespace Chat.Application.SharedChats.Results;

public sealed record RemixSharedChatResult
(
    Guid ChatId,
    string Title,
    DateTimeOffset CreatedAt
);