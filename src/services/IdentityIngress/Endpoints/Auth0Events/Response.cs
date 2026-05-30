namespace IdentityIngress.Endpoints.Auth0Events;

internal sealed class Response
{
    public required bool Accepted { get; init; }

    public required bool Published { get; init; }

    public string? EventType { get; init; }
}