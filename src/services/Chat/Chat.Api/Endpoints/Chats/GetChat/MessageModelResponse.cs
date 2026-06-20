namespace Chat.Api.Endpoints.Chats.GetChat;

internal sealed class MessageModelResponse
{
    public required Guid Id { get; init; }

    public required string? Slug { get; init; }

    public required string? Name { get; init; }
}