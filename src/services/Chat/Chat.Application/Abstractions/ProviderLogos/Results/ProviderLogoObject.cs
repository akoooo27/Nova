namespace Chat.Application.Abstractions.ProviderLogos.Results;

public sealed record ProviderLogoObject
(
    string Key,
    string FileName,
    string ContentType,
    long Size,
    DateTimeOffset? LastModified
);