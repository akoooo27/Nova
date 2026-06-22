namespace Chat.Api.Endpoints.SharedChats.GetSharedChat;

internal sealed class Response
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required Guid CurrentNode { get; init; }

    public required IReadOnlyDictionary<string, MappingNodeResponse> Mapping { get; init; }
}