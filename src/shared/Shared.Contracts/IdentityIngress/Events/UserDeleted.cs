namespace Shared.Contracts.IdentityIngress.Events;

public sealed class UserDeleted
{
    public required string EventId { get; init; }

    public required string Provider { get; init; }

    public required string ProviderUserId { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }
}