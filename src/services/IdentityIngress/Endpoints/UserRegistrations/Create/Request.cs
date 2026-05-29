namespace IdentityIngress.Endpoints.UserRegistrations.Create;

internal sealed class Request
{
    public required string Sub { get; init; }

    public string? Email { get; init; }

    public bool? EmailVerified { get; init; }

    public string? Name { get; init; }
}
