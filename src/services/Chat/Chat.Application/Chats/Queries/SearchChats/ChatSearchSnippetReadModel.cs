namespace Chat.Application.Chats.Queries.SearchChats;

public sealed record ChatSearchSnippetReadModel
(
    Guid MessageId,
    string Role,
    string Text
);