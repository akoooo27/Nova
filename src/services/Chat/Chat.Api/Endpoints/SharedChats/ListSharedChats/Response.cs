namespace Chat.Api.Endpoints.SharedChats.ListSharedChats;

internal sealed class Response
{
    public required IReadOnlyCollection<SharedChatListItemResponse> Items { get; init; }

    public required int Total { get; init; }

    public required int Limit { get; init; }

    public required int Offset { get; init; }
}