namespace Chat.Api.Endpoints.SharedChats.ListSharedChats;

internal sealed class SharedChatListItemResponse
{
    public required Guid Id { get; init; }

    public required string ShareUrl { get; init; }

    public required string Title { get; init; }

    public required Guid ChatId { get; init; }

    public required Guid CurrentMessageId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}