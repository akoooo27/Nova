using System.ComponentModel.DataAnnotations;

namespace Chat.Infrastructure.Options;

// User-managed Google integration. Bound and validated only in the API host
// (the connect/disconnect flow lives there); the turn worker uses Arcade tools
// but never these options, so they must not gate worker startup.
public sealed class GoogleIntegrationOptions
{
    public const string SectionName = "Arcade:GoogleIntegration";

    // The provider is configured once in the Arcade account; users only
    // connect/disconnect against it.
    [Required]
    public string ProviderId { get; init; } = "my-google-provider";

    // Source of truth is configuration (see appsettings). Defaulting to empty
    // avoids the config binder *merging* a code default into the bound values
    // (it appends rather than replaces); MinLength(1) fails fast if unset.
    [Required]
    [MinLength(1)]
    public IReadOnlyList<string> Scopes { get; init; } = [];

    // Where Arcade returns the browser after a completed connect (the frontend
    // integrations page). Server-configured to avoid open-redirect.
    [Required]
    public Uri PostConnectRedirectUri { get; init; } =
        new("https://localhost:7001/settings/integrations");
}