namespace Chat.Api.Endpoints.SharedChats.RemixSharedChat;

internal sealed class Response
{
    public required Guid ChatId { get; init; }

    public required string Title { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}