using System.ComponentModel.DataAnnotations;

namespace Chat.Infrastructure.Options;

public sealed class ArcadeOptions
{
    public const string SectionName = "Arcade";

    [Required]
    public Uri BaseUrl { get; init; } = new("https://api.arcade.dev");

    [Required]
    public string ApiKey { get; init; } = string.Empty;
}