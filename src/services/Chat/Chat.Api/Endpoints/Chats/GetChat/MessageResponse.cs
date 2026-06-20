namespace Chat.Api.Endpoints.Chats.GetChat;

internal sealed class MessageResponse
{
    public required string Role { get; init; }

    public required string? Content { get; init; }

    public required string Status { get; init; }

    public required string? FailureReason { get; init; }

    public required int SiblingIndex { get; init; }

    public required MessageModelResponse? Model { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset? CompletedAt { get; init; }
}