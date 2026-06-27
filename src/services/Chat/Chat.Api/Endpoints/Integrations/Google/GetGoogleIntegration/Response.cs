namespace Chat.Api.Endpoints.Integrations.Google.GetGoogleIntegration;

internal sealed record Response
(
    bool Connected,
    string? AccountEmail,
    IReadOnlyList<string> Scopes
);