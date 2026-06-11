namespace Chat.Application.Abstractions.ProviderLogos.Results;

public sealed record ProviderLogoUploadUrl
(
    Uri UploadUrl,
    string LogoKey,
    DateTimeOffset ExpiresAt,
    IReadOnlyDictionary<string, string> Headers
);