using ErrorOr;

namespace IdentityIngress.IdentityProviders.Auth0;

internal static class Auth0Errors
{
    public static Error UnsupportedEventType(string eventType) => Error.Validation
    (
        code: "Auth0.UnsupportedEventType",
        description: $"The Auth0 event type '{eventType}' is not supported."
    );

    public static Error InvalidPayload => Error.Validation
    (
        code: "Auth0.InvalidPayload",
        description: "The Auth0 event payload is invalid or missing required fields."
    );

    public static Error UnsupportedEventTypeMapped(string eventType) => Error.Failure
    (
        code: "Auth0.UnsupportedEventTypeMapped",
        description: $"No mapping exists for Auth0 event type '{eventType}'."
    );
}