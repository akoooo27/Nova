namespace Chat.Api.Endpoints.Chats.GetChats;

internal sealed class Response
{
    public required IReadOnlyCollection<ChatListItemResponse> Items { get; init; }

    public required int Total { get; init; }

    public required int Limit { get; init; }

    public required int Offset { get; init; }
}