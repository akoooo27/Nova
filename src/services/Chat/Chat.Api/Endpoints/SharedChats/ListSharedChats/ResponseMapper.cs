using Chat.Api.SharedChats;
using Chat.Application.SharedChats.Queries.GetSharedChats;

namespace Chat.Api.Endpoints.SharedChats.ListSharedChats;

internal static class ResponseMapper
{
    public static Response ToResponse(SharedChatListReadModel readModel, SharedLinkUrlBuilder urlBuilder) => new()
    {
        Items = readModel.SharedChats
            .Select(sharedChat => ToResponse(sharedChat, urlBuilder))
            .ToList(),
        Total = readModel.Total,
        Limit = readModel.Limit,
        Offset = readModel.Offset
    };

    private static SharedChatListItemResponse ToResponse
    (
        SharedChatSummaryReadModel sharedChat,
        SharedLinkUrlBuilder urlBuilder
    ) => new()
    {
        Id = sharedChat.Id,
        ShareUrl = urlBuilder.Build(sharedChat.Id),
        Title = sharedChat.Title,
        ChatId = sharedChat.ChatId,
        CurrentMessageId = sharedChat.CurrentMessageId,
        CreatedAt = sharedChat.CreatedAt
    };
}