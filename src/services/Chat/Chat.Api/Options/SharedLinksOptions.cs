using System.ComponentModel.DataAnnotations;

namespace Chat.Api.Options;

internal sealed class SharedLinksOptions
{
    public const string SectionName = "SharedLinks";

    [Required]
    [Url]
    public required string PublicBaseUrl { get; init; }
}