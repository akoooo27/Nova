using System.ComponentModel.DataAnnotations;

namespace Shared.Infrastructure.Options;

public sealed class Auth0JwtOptions
{
    public const string SectionName = "Auth0";

    [Required]
    public string Domain { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;
}