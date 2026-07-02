namespace Chat.Api.Endpoints.Chats.SearchChats;

internal sealed class SearchChatResultResponse
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public required bool IsPinned { get; init; }

    public required DateTimeOffset? PinnedAt { get; init; }

    public required bool IsArchived { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required int MatchCount { get; init; }

    public required IReadOnlyCollection<SearchChatSnippetResponse> Snippets { get; init; }
}