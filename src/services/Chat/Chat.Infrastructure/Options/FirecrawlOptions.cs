using System.ComponentModel.DataAnnotations;

namespace Chat.Infrastructure.Options;

public sealed class FirecrawlOptions
{
    public const string SectionName = "Firecrawl";

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    public Uri BaseUrl { get; init; } = new("https://api.firecrawl.dev");
}