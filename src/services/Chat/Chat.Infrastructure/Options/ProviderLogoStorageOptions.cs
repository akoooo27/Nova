using System.ComponentModel.DataAnnotations;

namespace Chat.Infrastructure.Options;

internal sealed class ProviderLogoStorageOptions
{
    public const string SectionName = "ProviderLogoStorage";

    [Required]
    public required string BucketName { get; init; }

    [Required]
    public string Prefix { get; init; } = "providers/";

    [Range(1, int.MaxValue)]
    public int PresignedUrlExpirationMinutes { get; init; } = 10;

    public string NormalizedPrefix => Prefix.Trim().Trim('/') + "/";
}