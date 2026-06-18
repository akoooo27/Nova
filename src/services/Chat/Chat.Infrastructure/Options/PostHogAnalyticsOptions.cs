using System.ComponentModel.DataAnnotations;

namespace Chat.Infrastructure.Options;

internal sealed class PostHogAnalyticsOptions
{
    public const string SectionName = "PostHog";

    [Required]
    public string ProjectApiKey { get; init; } = string.Empty;

    [Required]
    public Uri HostUrl { get; init; } = new("https://eu.i.posthog.com");
}