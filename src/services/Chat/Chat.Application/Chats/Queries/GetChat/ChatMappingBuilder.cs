namespace Chat.Application.Chats.Queries.GetChat;

public static class ChatMappingBuilder
{
    public static IReadOnlyList<ChatMappingNodeReadModel> Build(IReadOnlyList<ChatMessageReadModel> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        ILookup<Guid?, ChatMessageReadModel> messagesByParent = messages.ToLookup
        (
            message => message.ParentMessageId
        );

        return messages
            .Select(message => new ChatMappingNodeReadModel
            (
                Id: message.Id,
                ParentMessageId: message.ParentMessageId,
                ChildrenIds: messagesByParent[message.Id]
                    .OrderBy(child => child.SiblingIndex)
                    .ThenBy(child => child.CreatedAt)
                    .ThenBy(child => child.Id)
                    .Select(child => child.Id)
                    .ToList(),
                Message: message
            ))
            .ToList();
    }
}