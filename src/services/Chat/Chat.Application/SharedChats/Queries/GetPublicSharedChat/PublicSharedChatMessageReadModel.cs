using Chat.Domain.Chats.ValueObjects;

namespace Chat.Application.SharedChats.Queries.GetPublicSharedChat;

public sealed record PublicSharedChatMessageReadModel
(
    Guid Id,
    Guid? ParentMessageId,
    MessageRole Role,
    string? Content,
    MessageStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt
);