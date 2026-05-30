using System.ComponentModel.DataAnnotations;

namespace IdentityIngress.Options;

internal sealed class Auth0EventsOptions
{
    public const string SectionName = "Auth0Events";

    [Required]
    [MinLength(1)]
    public required string WebhookToken { get; init; }
}