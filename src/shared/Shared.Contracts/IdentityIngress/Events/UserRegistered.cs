namespace Shared.Contracts.IdentityIngress.Events;

public sealed class UserRegistered
{
    public required string Sub { get; init; }

    public string? Email { get; init; }

    public bool? EmailVerified { get; init; }

    public string? Name { get; init; }
}
