namespace Chat.Api.Endpoints.Chats.GetChat;

internal sealed class MappingNodeResponse
{
    public required Guid Id { get; init; }

    public required Guid? ParentId { get; init; }

    public required IReadOnlyList<Guid> Children { get; init; }

    public required MessageResponse Message { get; init; }
}