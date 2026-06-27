namespace Chat.Application.Abstractions.Arcade.Google;

public sealed record GoogleIntegrationStatus
(
    bool Connected,
    string? AccountEmail,
    IReadOnlyList<string> Scopes
);