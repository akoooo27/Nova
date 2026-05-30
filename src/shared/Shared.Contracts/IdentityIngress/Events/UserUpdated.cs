namespace Shared.Contracts.IdentityIngress.Events;

public sealed class UserUpdated
{
    public required string EventId { get; init; }

    public required string Provider { get; init; }

    public required string ProviderUserId { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public string? Email { get; init; }

    public bool? EmailVerified { get; init; }

    public string? Name { get; init; }
}