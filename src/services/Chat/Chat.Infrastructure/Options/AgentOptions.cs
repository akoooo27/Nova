using System.ComponentModel.DataAnnotations;

namespace Chat.Infrastructure.Options;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    [Required]
    public Uri BaseUrl { get; init; } = new("https://openrouter.ai/api/v1");

    [Required]
    public string ApiKey { get; init; } = string.Empty;
}