namespace Chat.Application.Chats.Results;

public sealed record TurnStartedResult
(
    Guid ChatId,
    Guid UserMessageId,
    Guid AssistantMessageId
);