namespace Chat.Application.Chats.Queries.GetChat;

public sealed record ChatMappingNodeReadModel
(
    Guid Id,
    Guid? ParentMessageId,
    IReadOnlyList<Guid> ChildrenIds,
    ChatMessageReadModel Message
);