namespace Chat.Application.Abstractions.Arcade.Google;

public sealed record GoogleConnectResult
(
    bool Connected,
    Uri? AuthorizationUrl
);