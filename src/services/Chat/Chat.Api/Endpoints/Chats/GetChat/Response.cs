namespace Chat.Api.Endpoints.Chats.GetChat;

internal sealed class Response
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public required bool IsPinned { get; init; }

    public required DateTimeOffset? PinnedAt { get; init; }

    public required bool IsArchived { get; init; }

    public required bool IsTemporary { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required Guid CurrentNode { get; init; }

    public required IReadOnlyDictionary<string, MappingNodeResponse> Mapping { get; init; }
}