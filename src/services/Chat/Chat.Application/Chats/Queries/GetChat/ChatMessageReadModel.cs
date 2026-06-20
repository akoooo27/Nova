using Chat.Domain.Chats.ValueObjects;

namespace Chat.Application.Chats.Queries.GetChat;

public sealed record ChatMessageReadModel
(
    Guid Id,
    Guid? ParentMessageId,
    MessageRole Role,
    string? Content,
    MessageStatus Status,
    string? FailureReason,
    int SiblingIndex,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    ChatMessageModelReadModel? Model
);