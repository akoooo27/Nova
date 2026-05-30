namespace IdentityIngress.Endpoints.Auth0Events;

internal sealed class Request
{
    public required string Id { get; init; }

    public required string Type { get; init; }

    public required DateTimeOffset Time { get; init; }

    public required Auth0EventData Data { get; init; }
}