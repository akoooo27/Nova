using System.Collections.Frozen;

using Amazon.S3;
using Amazon.S3.Model;

using Chat.Application.Abstractions.ProviderLogos;
using Chat.Application.Abstractions.ProviderLogos.Errors;
using Chat.Application.Abstractions.ProviderLogos.Results;
using Chat.Infrastructure.Options;

using ErrorOr;

using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.ProviderLogos;

internal sealed class S3ProviderLogoStorage(IAmazonS3 s3, IOptions<ProviderLogoStorageOptions> options)
    : IProviderLogoStorage
{
    private static readonly FrozenDictionary<string, string> ExtensionsByContentType =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/svg+xml"] = "svg",
            ["image/png"] = "png",
            ["image/webp"] = "webp"
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> ContentTypesByExtension =
        ExtensionsByContentType.ToFrozenDictionary
        (
            pair => pair.Value,
            pair => pair.Key,
            StringComparer.OrdinalIgnoreCase
        );

    private readonly ProviderLogoStorageOptions _options = options.Value;

    public async Task<ErrorOr<ProviderLogoUploadUrl>> CreateUploadUrlAsync
    (
        string providerSlug,
        string contentType,
        CancellationToken cancellationToken
    )
    {
        string normalizedContentType = contentType.Trim();

        if (!ExtensionsByContentType.TryGetValue(normalizedContentType, out string? extension))
        {
            return ProviderLogoOperationErrors.UnsupportedContentType(normalizedContentType);
        }

        string key = $"{_options.NormalizedPrefix}{providerSlug}/logo.{extension}";
        DateTime expiresAt = DateTime.UtcNow.AddMinutes(_options.PresignedUrlExpirationMinutes);

        GetPreSignedUrlRequest request = new()
        {
            BucketName = _options.BucketName,
            Key = key,
            ContentType = normalizedContentType,
            Verb = HttpVerb.PUT,
            Expires = expiresAt,
        };

        string uploadUrl = await s3.GetPreSignedURLAsync(request);

        ProviderLogoUploadUrl result = new
        (
            UploadUrl: new Uri(uploadUrl),
            LogoKey: key,
            ExpiresAt: expiresAt,
            Headers: new Dictionary<string, string> { ["Content-Type"] = normalizedContentType }
        );

        return result;
    }

    public async Task<IReadOnlyCollection<ProviderLogoObject>> ListAsync(CancellationToken cancellationToken)
    {
        ListObjectsV2Request request = new()
        {
            BucketName = _options.BucketName,
            Prefix = _options.NormalizedPrefix,
        };

        IListObjectsV2Paginator paginator = s3.Paginators.ListObjectsV2(request);

        List<ProviderLogoObject> logos = [];

        await foreach (S3Object obj in paginator.S3Objects.WithCancellation(cancellationToken))
        {
            if (!obj.Key.EndsWith('/'))
            {
                logos.Add(ToProviderLogoObject(obj));
            }
        }

        IReadOnlyCollection<ProviderLogoObject> result = logos;

        return result;
    }

    private static ProviderLogoObject ToProviderLogoObject(S3Object obj)
    {
        string fileName = obj.Key[(obj.Key.LastIndexOf('/') + 1)..];

        return new ProviderLogoObject
        (
            Key: obj.Key,
            FileName: fileName,
            ContentType: ResolveContentType(fileName),
            Size: obj.Size ?? 0,
            LastModified: obj.LastModified
        );
    }

    private static string ResolveContentType(string fileName)
    {
        string extension = Path.GetExtension(fileName).TrimStart('.');

        return ContentTypesByExtension.GetValueOrDefault(extension, "application/octet-stream");
    }
}