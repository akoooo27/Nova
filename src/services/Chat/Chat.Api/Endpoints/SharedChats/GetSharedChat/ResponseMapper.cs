using Chat.Application.SharedChats.Queries.GetPublicSharedChat;

namespace Chat.Api.Endpoints.SharedChats.GetSharedChat;

internal static class ResponseMapper
{
    public static Response ToResponse(PublicSharedChatReadModel readModel)
    {
        Dictionary<string, MappingNodeResponse> mapping = readModel.Messages
            .Select((message, index) => ToMappingNode(message, index, readModel.Messages))
            .ToDictionary(node => node.Id.ToString());

        return new Response
        {
            Id = readModel.Id,
            Title = readModel.Title,
            CreatedAt = readModel.CreatedAt,
            CurrentNode = readModel.CurrentMessageId,
            Mapping = mapping,
            AllowRemix = readModel.AllowRemix
        };
    }

    private static MappingNodeResponse ToMappingNode
    (
        PublicSharedChatMessageReadModel message,
        int index,
        IReadOnlyList<PublicSharedChatMessageReadModel> messages
    ) => new()
    {
        Id = message.Id,
        ParentId = message.ParentMessageId,
        Children = index + 1 < messages.Count ? [messages[index + 1].Id] : [],
        Message = new MessageResponse
        {
            Role = message.Role.ToString().ToLowerInvariant(),
            Content = message.Content,
            Status = message.Status.ToString().ToLowerInvariant(),
            CreatedAt = message.CreatedAt,
            CompletedAt = message.CompletedAt
        }
    };
}