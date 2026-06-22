namespace Chat.Application.SharedChats.Queries.GetSharedChats;

public sealed record SharedChatSummaryReadModel
(
    Guid Id,
    string Title,
    Guid ChatId,
    Guid CurrentMessageId,
    DateTimeOffset CreatedAt
);