using System.ComponentModel.DataAnnotations;

namespace Chat.Infrastructure.Options;

public sealed class ExaOptions
{
    public const string SectionName = "Exa";

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    public Uri BaseUrl { get; init; } = new("https://api.exa.ai");
}