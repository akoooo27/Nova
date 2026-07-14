namespace Chat.Api.Endpoints.SharedChats.GetSharedChat;

internal sealed class MessageResponse
{
    public required string Role { get; init; }

    public required string? Content { get; init; }

    public required string Status { get; init; }

    public required string Kind { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset? CompletedAt { get; init; }
}