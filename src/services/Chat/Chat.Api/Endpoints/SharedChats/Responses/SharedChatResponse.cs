using Chat.Application.SharedChats.Results;

namespace Chat.Api.Endpoints.SharedChats.Responses;

internal sealed record SharedChatResponse
(
    Guid ShareId,
    string ShareUrl,
    string Title,
    Guid ChatId,
    Guid CurrentMessageId,
    DateTimeOffset CreatedAt,
    bool AllowRemix,
    bool AlreadyExists
)
{
    public static SharedChatResponse From(SharedChatResult result, string shareUrl) => new
    (
        ShareId: result.Id,
        ShareUrl: shareUrl,
        Title: result.Title,
        ChatId: result.ChatId,
        CurrentMessageId: result.CurrentMessageId,
        CreatedAt: result.CreatedAt,
        AllowRemix: result.AllowRemix,
        AlreadyExists: result.AlreadyExists
    );
}