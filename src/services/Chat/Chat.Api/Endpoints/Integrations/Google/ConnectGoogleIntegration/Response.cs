namespace Chat.Api.Endpoints.Integrations.Google.ConnectGoogleIntegration;

internal sealed record Response
(
    bool Connected,
    Uri? AuthorizationUrl
);