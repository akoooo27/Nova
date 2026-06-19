namespace Chat.Application.Chats.Queries.GetChat;

public sealed record ChatMessageModelReadModel
(
    Guid Id,
    string? Slug,
    string? Name
);