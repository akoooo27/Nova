namespace Chat.Application.SharedChats.Queries.GetPublicSharedChat;

public sealed record PublicSharedChatReadModel
(
    Guid Id,
    string Title,
    DateTimeOffset CreatedAt,
    Guid CurrentMessageId,
    bool AllowRemix,
    IReadOnlyList<PublicSharedChatMessageReadModel> Messages
);