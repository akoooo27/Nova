using Chat.Application.Chats.Queries.GetChat;
using Chat.Domain.Chats.ValueObjects;

namespace Chat.Application.Tests.Chats.Queries;

public sealed class ChatMappingBuilderTests
{
    [Fact]
    public void BuildWiresRootParentAndChildren()
    {
        Guid root = Guid.CreateVersion7();
        Guid firstAssistant = Guid.CreateVersion7();
        Guid secondAssistant = Guid.CreateVersion7();

        ChatMessageReadModel[] messages =
        [
            Message(root, parent: null, sibling: 0, role: MessageRole.User),
            Message(firstAssistant, parent: root, sibling: 0, role: MessageRole.Assistant),
            Message(secondAssistant, parent: root, sibling: 1, role: MessageRole.Assistant)
        ];

        IReadOnlyList<ChatMappingNodeReadModel> nodes = ChatMappingBuilder.Build(messages);

        ChatMappingNodeReadModel rootNode = nodes.Single(node => node.Id == root);
        Assert.Null(rootNode.ParentMessageId);
        Assert.Equal(new[] { firstAssistant, secondAssistant }, rootNode.ChildrenIds);

        Assert.Empty(nodes.Single(node => node.Id == firstAssistant).ChildrenIds);
        Assert.Empty(nodes.Single(node => node.Id == secondAssistant).ChildrenIds);
    }

    [Fact]
    public void BuildOrdersChildrenBySiblingIndexNotCreatedAt()
    {
        Guid root = Guid.CreateVersion7();
        Guid first = Guid.CreateVersion7();
        Guid second = Guid.CreateVersion7();

        ChatMessageReadModel[] messages =
        [
            Message(root, parent: null, sibling: 0, role: MessageRole.User),
            Message
            (
                second,
                parent: root,
                sibling: 1,
                role: MessageRole.Assistant,
                createdAt: DateTimeOffset.UtcNow.AddMinutes(-5)
            ),
            Message
            (
                first,
                parent: root,
                sibling: 0,
                role: MessageRole.Assistant,
                createdAt: DateTimeOffset.UtcNow
            )
        ];

        IReadOnlyList<ChatMappingNodeReadModel> nodes = ChatMappingBuilder.Build(messages);

        Assert.Equal(new[] { first, second }, nodes.Single(node => node.Id == root).ChildrenIds);
    }

    private static ChatMessageReadModel Message
    (
        Guid id,
        Guid? parent,
        int sibling,
        MessageRole role,
        DateTimeOffset? createdAt = null
    ) => new
    (
        Id: id,
        ParentMessageId: parent,
        Role: role,
        Content: "x",
        Status: MessageStatus.Completed,
        FailureReason: null,
        SiblingIndex: sibling,
        CreatedAt: createdAt ?? DateTimeOffset.UtcNow,
        CompletedAt: null,
        Model: null,
        Kind: MessageKind.Text,
        AgentRun: null
    );
}