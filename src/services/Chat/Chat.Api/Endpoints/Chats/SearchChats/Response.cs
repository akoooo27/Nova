namespace Chat.Api.Endpoints.Chats.SearchChats;

internal sealed class Response
{
    public required IReadOnlyCollection<SearchChatResultResponse> Items { get; init; }

    public required int Total { get; init; }

    public required int Limit { get; init; }

    public required int Offset { get; init; }
}