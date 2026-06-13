using Chat.Application.Chats.Results;

namespace Chat.Api.Endpoints.Chats.Responses;

internal sealed record TurnStartedResponse
(
    Guid ChatId,
    Guid UserMessageId,
    Guid AssistantMessageId,
    string StreamPath
)
{
    public static TurnStartedResponse From(TurnStartedResult result) => new
    (
        ChatId: result.ChatId,
        UserMessageId: result.UserMessageId,
        AssistantMessageId: result.AssistantMessageId,
        StreamPath: $"/v1/chats/{result.ChatId}/turns/{result.AssistantMessageId}/stream"
    );
}