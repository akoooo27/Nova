using System.Text.Json.Serialization;

namespace IdentityIngress.Endpoints.Auth0Events;

internal sealed class Auth0UserObject
{
    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    public string? Email { get; init; }

    [JsonPropertyName("email_verified")]
    public bool? EmailVerified { get; init; }

    public string? Name { get; init; }
}