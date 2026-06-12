namespace Chat.Application.Turns;

public sealed record TurnRequested
(
    Guid ChatId,
    string UserId,
    Guid AssistantMessageId
);