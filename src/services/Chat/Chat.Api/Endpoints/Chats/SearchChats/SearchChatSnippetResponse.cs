namespace Chat.Api.Endpoints.Chats.SearchChats;

internal sealed class SearchChatSnippetResponse
{
    public required Guid MessageId { get; init; }

    public required string Role { get; init; }

    public required string Text { get; init; }
}