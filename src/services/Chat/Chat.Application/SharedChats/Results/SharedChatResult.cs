namespace Chat.Application.SharedChats.Results;

public sealed record SharedChatResult
(
    Guid Id,
    string Title,
    Guid ChatId,
    Guid CurrentMessageId,
    DateTimeOffset CreatedAt,
    bool AlreadyExists
);