namespace IdentityIngress.IdentityProviders;

internal sealed record MappedIdentityEvent
(
    object Event,
    string EventType
);