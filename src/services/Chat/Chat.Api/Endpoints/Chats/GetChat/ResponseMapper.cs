using Chat.Application.Chats.Queries.GetChat;

namespace Chat.Api.Endpoints.Chats.GetChat;

internal static class ResponseMapper
{
    public static Response ToResponse(ChatDetailReadModel readModel)
    {
        IReadOnlyList<ChatMappingNodeReadModel> nodes = ChatMappingBuilder.Build(readModel.Messages);

        Dictionary<string, MappingNodeResponse> mapping = nodes.ToDictionary
        (
            node => node.Id.ToString(),
            ToMappingNode
        );

        return new Response
        {
            Id = readModel.Id,
            Title = readModel.Title,
            IsPinned = readModel.IsPinned,
            PinnedAt = readModel.PinnedAt,
            IsArchived = readModel.IsArchived,
            IsTemporary = readModel.IsTemporary,
            CreatedAt = readModel.CreatedAt,
            UpdatedAt = readModel.UpdatedAt,
            CurrentNode = readModel.CurrentMessageId,
            Mapping = mapping
        };
    }

    private static MappingNodeResponse ToMappingNode(ChatMappingNodeReadModel node) => new()
    {
        Id = node.Id,
        ParentId = node.ParentMessageId,
        Children = node.ChildrenIds,
        Message = ToMessage(node.Message)
    };

    private static MessageResponse ToMessage(ChatMessageReadModel message) => new()
    {
        Role = message.Role.ToString().ToLowerInvariant(),
        Content = message.Content,
        Status = message.Status.ToString().ToLowerInvariant(),
        FailureReason = message.FailureReason,
        SiblingIndex = message.SiblingIndex,
        Model = message.Model is null
            ? null
            : new MessageModelResponse
            {
                Id = message.Model.Id,
                Slug = message.Model.Slug,
                Name = message.Model.Name
            },
        CreatedAt = message.CreatedAt,
        CompletedAt = message.CompletedAt
    };
}