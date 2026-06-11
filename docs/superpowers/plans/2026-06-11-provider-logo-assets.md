# Provider Logo Assets Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add backend APIs for model catalog managers to request presigned S3 upload URLs, list provider logo files, and delete provider logo files under `providers/`.

**Architecture:** Keep provider logo files as infrastructure assets, not model catalog domain objects. Application handlers validate provider identity and authorization flow, then call an `IProviderLogoStorage` abstraction implemented by S3 infrastructure. Existing provider create/update APIs remain responsible for assigning or clearing `LlmProvider.LogoKey`.

**Tech Stack:** .NET 10, FastEndpoints, Mediator.SourceGenerator, ErrorOr, FluentValidation, AWS SDK for S3, existing Chat service layering.

---

## Scope Notes

This plan intentionally does not add or expand tests because `AGENTS.md` says test work requires explicit user approval. Verification uses restore/build commands only.

This plan intentionally does not include commit steps because the user explicitly said there is no need for commits.

Storage abstractions use `Task<T>`, not `ValueTask<T>`. The Mediator handlers still return `ValueTask<T>` because that is the `Mediator.SourceGenerator` handler contract.

Storage methods return `ErrorOr<T>` only for expected, user-correctable policy failures such as unsupported content types or invalid keys outside the provider-logo prefix. AWS credentials, network failures, S3 service errors, and other unexpected infrastructure failures should throw and flow through the existing global exception handling.

`AWSSDK.S3` version `4.0.24.3` was checked against NuGet package metadata on 2026-06-11.

## File Structure

- Modify `Directory.Packages.props`
  - Add central package version for `AWSSDK.S3`.
- Modify `src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj`
  - Reference `AWSSDK.S3`.
- Create `src/services/Chat/Chat.Application/Abstractions/ProviderLogos/IProviderLogoStorage.cs`
  - Application abstraction for presign/list/delete.
- Create `src/services/Chat/Chat.Application/Abstractions/ProviderLogos/ProviderLogoUploadUrl.cs`
  - Application-facing upload URL result.
- Create `src/services/Chat/Chat.Application/Abstractions/ProviderLogos/ProviderLogoObject.cs`
  - Application-facing listed object result.
- Create `src/services/Chat/Chat.Application/ModelCatalog/ProviderLogos/Errors/ProviderLogoOperationErrors.cs`
  - Reusable validation/storage errors.
- Create `src/services/Chat/Chat.Application/ModelCatalog/ProviderLogos/Commands/RequestProviderLogoUploadUrl/RequestProviderLogoUploadUrlCommand.cs`
  - Mediator command for presigned upload URL creation.
- Create `src/services/Chat/Chat.Application/ModelCatalog/ProviderLogos/Commands/RequestProviderLogoUploadUrl/RequestProviderLogoUploadUrlCommandValidator.cs`
  - Request validation for content type shape.
- Create `src/services/Chat/Chat.Application/ModelCatalog/ProviderLogos/Commands/RequestProviderLogoUploadUrl/RequestProviderLogoUploadUrlHandler.cs`
  - Loads provider and delegates upload URL creation.
- Create `src/services/Chat/Chat.Application/ModelCatalog/ProviderLogos/Queries/ListProviderLogos/ListProviderLogosQuery.cs`
  - Mediator query for listing logos.
- Create `src/services/Chat/Chat.Application/ModelCatalog/ProviderLogos/Queries/ListProviderLogos/ListProviderLogosHandler.cs`
  - Delegates logo listing.
- Create `src/services/Chat/Chat.Application/ModelCatalog/ProviderLogos/Commands/DeleteProviderLogo/DeleteProviderLogoCommand.cs`
  - Mediator command for deleting a logo key.
- Create `src/services/Chat/Chat.Application/ModelCatalog/ProviderLogos/Commands/DeleteProviderLogo/DeleteProviderLogoCommandValidator.cs`
  - Request validation for key shape.
- Create `src/services/Chat/Chat.Application/ModelCatalog/ProviderLogos/Commands/DeleteProviderLogo/DeleteProviderLogoHandler.cs`
  - Delegates deletion to storage.
- Create `src/services/Chat/Chat.Infrastructure/ProviderLogos/ProviderLogoStorageOptions.cs`
  - S3 bucket, prefix, expiry, and optional public base URL settings.
- Create `src/services/Chat/Chat.Infrastructure/ProviderLogos/S3ProviderLogoStorage.cs`
  - AWS S3 implementation of `IProviderLogoStorage`.
- Modify `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`
  - Register options, `IAmazonS3`, and `IProviderLogoStorage`.
- Modify `src/services/Chat/Chat.Api/appsettings.json`
  - Add default provider logo storage configuration.
- Modify `src/services/Chat/Chat.Api/appsettings.Development.json`
  - Add dev bucket and prefix configuration.
- Create `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/RequestProviderLogoUploadUrl/Request.cs`
  - API request contract.
- Create `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/RequestProviderLogoUploadUrl/Response.cs`
  - API response contract.
- Create `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/RequestProviderLogoUploadUrl/Endpoint.cs`
  - FastEndpoint for `POST /model-catalog/providers/{providerId}/logo-upload-url`.
- Create `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/ListProviderLogos/ProviderLogoResponse.cs`
  - Listed logo response contract.
- Create `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/ListProviderLogos/Response.cs`
  - List response contract.
- Create `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/ListProviderLogos/Endpoint.cs`
  - FastEndpoint for `GET /model-catalog/provider-logos`.
- Create `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/DeleteProviderLogo/Endpoint.cs`
  - FastEndpoint for `DELETE /model-catalog/provider-logos?key=...`.

## Task 1: Add S3 Dependency And Storage Configuration

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj`
- Modify: `src/services/Chat/Chat.Api/appsettings.json`
- Modify: `src/services/Chat/Chat.Api/appsettings.Development.json`

- [ ] **Step 1: Add the central package version**

In `Directory.Packages.props`, add this package version inside the `<!-- Infrastructure -->` group:

```xml
<PackageVersion Include="AWSSDK.S3" Version="4.0.24.3" />
```

- [ ] **Step 2: Reference the package from Chat.Infrastructure**

In `src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj`, add this package reference inside the main package `ItemGroup`:

```xml
<PackageReference Include="AWSSDK.S3" />
```

- [ ] **Step 3: Add default configuration**

In `src/services/Chat/Chat.Api/appsettings.json`, add `ProviderLogoStorage` after `AllowedHosts`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ProviderLogoStorage": {
    "BucketName": "nova-public-assets-dev",
    "Prefix": "providers/",
    "PresignedUrlExpirationMinutes": 10
  }
}
```

- [ ] **Step 4: Add development configuration**

In `src/services/Chat/Chat.Api/appsettings.Development.json`, add the same section:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "ProviderLogoStorage": {
    "BucketName": "nova-public-assets-dev",
    "Prefix": "providers/",
    "PresignedUrlExpirationMinutes": 10
  }
}
```

Do not add `PublicBaseUrl` unless the actual CDN base URL is known. The storage implementation handles it as optional.

## Task 2: Add Application Storage Abstraction And Error Types

**Files:**
- Create: `src/services/Chat/Chat.Application/Abstractions/ProviderLogos/IProviderLogoStorage.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/ProviderLogos/ProviderLogoUploadUrl.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/ProviderLogos/ProviderLogoObject.cs`
- Create: `src/services/Chat/Chat.Application/ModelCatalog/ProviderLogos/Errors/ProviderLogoOperationErrors.cs`

- [ ] **Step 1: Add upload URL result**

Create `ProviderLogoUploadUrl.cs`:

```csharp
namespace Chat.Application.Abstractions.ProviderLogos;

public sealed record ProviderLogoUploadUrl
(
    Uri UploadUrl,
    string LogoKey,
    DateTimeOffset ExpiresAt,
    IReadOnlyDictionary<string, string> Headers
);
```

- [ ] **Step 2: Add listed object result**

Create `ProviderLogoObject.cs`:

```csharp
namespace Chat.Application.Abstractions.ProviderLogos;

public sealed record ProviderLogoObject
(
    string Key,
    string FileName,
    string ContentType,
    long Size,
    DateTimeOffset? LastModified,
    Uri? PublicUrl
);
```

- [ ] **Step 3: Add storage abstraction**

Create `IProviderLogoStorage.cs`:

```csharp
using ErrorOr;

namespace Chat.Application.Abstractions.ProviderLogos;

public interface IProviderLogoStorage
{
    Task<ErrorOr<ProviderLogoUploadUrl>> CreateUploadUrlAsync
    (
        string providerSlug,
        string contentType,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyCollection<ProviderLogoObject>> ListAsync(CancellationToken cancellationToken);

    Task<ErrorOr<Success>> DeleteAsync(string key, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Add provider logo errors**

Create `ProviderLogoOperationErrors.cs`:

```csharp
using ErrorOr;

namespace Chat.Application.ModelCatalog.ProviderLogos.Errors;

public static class ProviderLogoOperationErrors
{
    public static Error UnsupportedContentType(string contentType) =>
        Error.Validation
        (
            code: "ProviderLogo.UnsupportedContentType",
            description: $"Provider logos must be uploaded as image/svg+xml, image/png, or image/webp. Received '{contentType}'."
        );

    public static Error InvalidKey(string key) =>
        Error.Validation
        (
            code: "ProviderLogo.InvalidKey",
            description: $"Provider logo key '{key}' is invalid."
        );
}
```

## Task 3: Add Upload URL Application Command

**Files:**
- Create: `src/services/Chat/Chat.Application/ModelCatalog/ProviderLogos/Commands/RequestProviderLogoUploadUrl/RequestProviderLogoUploadUrlCommand.cs`
- Create: `src/services/Chat/Chat.Application/ModelCatalog/ProviderLogos/Commands/RequestProviderLogoUploadUrl/RequestProviderLogoUploadUrlCommandValidator.cs`
- Create: `src/services/Chat/Chat.Application/ModelCatalog/ProviderLogos/Commands/RequestProviderLogoUploadUrl/RequestProviderLogoUploadUrlHandler.cs`

- [ ] **Step 1: Add command**

Create `RequestProviderLogoUploadUrlCommand.cs`:

```csharp
using Chat.Application.Abstractions.ProviderLogos;

using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.ProviderLogos.Commands.RequestProviderLogoUploadUrl;

public sealed record RequestProviderLogoUploadUrlCommand
(
    Guid ProviderId,
    string ContentType
) : ICommand<ErrorOr<ProviderLogoUploadUrl>>;
```

- [ ] **Step 2: Add validator**

Create `RequestProviderLogoUploadUrlCommandValidator.cs`:

```csharp
using FluentValidation;

namespace Chat.Application.ModelCatalog.ProviderLogos.Commands.RequestProviderLogoUploadUrl;

internal sealed class RequestProviderLogoUploadUrlCommandValidator
    : AbstractValidator<RequestProviderLogoUploadUrlCommand>
{
    public RequestProviderLogoUploadUrlCommandValidator()
    {
        RuleFor(x => x.ProviderId)
            .NotEmpty();

        RuleFor(x => x.ContentType)
            .NotEmpty()
            .MaximumLength(128);
    }
}
```

- [ ] **Step 3: Add handler**

Create `RequestProviderLogoUploadUrlHandler.cs`:

```csharp
using Chat.Application.Abstractions.ProviderLogos;
using Chat.Application.ModelCatalog.LlmProviders.Errors;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.ProviderLogos.Commands.RequestProviderLogoUploadUrl;

internal sealed class RequestProviderLogoUploadUrlHandler
(
    ILlmProviderRepository providers,
    IProviderLogoStorage storage
) : ICommandHandler<RequestProviderLogoUploadUrlCommand, ErrorOr<ProviderLogoUploadUrl>>
{
    public async ValueTask<ErrorOr<ProviderLogoUploadUrl>> Handle
    (
        RequestProviderLogoUploadUrlCommand command,
        CancellationToken cancellationToken
    )
    {
        ErrorOr<LlmProviderId> providerIdResult = LlmProviderId.Create(command.ProviderId);

        if (providerIdResult.IsError)
        {
            return providerIdResult.Errors;
        }

        LlmProvider? provider = await providers.GetByIdAsync(providerIdResult.Value, cancellationToken);

        if (provider is null)
        {
            return LlmProviderOperationErrors.ProviderNotFound(providerIdResult.Value);
        }

        return await storage.CreateUploadUrlAsync
        (
            providerSlug: provider.Slug.Value,
            contentType: command.ContentType,
            cancellationToken: cancellationToken
        );
    }
}
```

## Task 4: Add List Provider Logos Query

**Files:**
- Create: `src/services/Chat/Chat.Application/ModelCatalog/ProviderLogos/Queries/ListProviderLogos/ListProviderLogosQuery.cs`
- Create: `src/services/Chat/Chat.Application/ModelCatalog/ProviderLogos/Queries/ListProviderLogos/ListProviderLogosHandler.cs`

- [ ] **Step 1: Add query**

Create `ListProviderLogosQuery.cs`:

```csharp
using Chat.Application.Abstractions.ProviderLogos;

using Mediator;

namespace Chat.Application.ModelCatalog.ProviderLogos.Queries.ListProviderLogos;

public sealed record ListProviderLogosQuery : IQuery<IReadOnlyCollection<ProviderLogoObject>>;
```

- [ ] **Step 2: Add handler**

Create `ListProviderLogosHandler.cs`:

```csharp
using Chat.Application.Abstractions.ProviderLogos;

using Mediator;

namespace Chat.Application.ModelCatalog.ProviderLogos.Queries.ListProviderLogos;

internal sealed class ListProviderLogosHandler(IProviderLogoStorage storage)
    : IQueryHandler<ListProviderLogosQuery, IReadOnlyCollection<ProviderLogoObject>>
{
    public async ValueTask<IReadOnlyCollection<ProviderLogoObject>> Handle
    (
        ListProviderLogosQuery query,
        CancellationToken cancellationToken
    ) => await storage.ListAsync(cancellationToken);
}
```

## Task 5: Add Delete Provider Logo Command

**Files:**
- Create: `src/services/Chat/Chat.Application/ModelCatalog/ProviderLogos/Commands/DeleteProviderLogo/DeleteProviderLogoCommand.cs`
- Create: `src/services/Chat/Chat.Application/ModelCatalog/ProviderLogos/Commands/DeleteProviderLogo/DeleteProviderLogoCommandValidator.cs`
- Create: `src/services/Chat/Chat.Application/ModelCatalog/ProviderLogos/Commands/DeleteProviderLogo/DeleteProviderLogoHandler.cs`

- [ ] **Step 1: Add command**

Create `DeleteProviderLogoCommand.cs`:

```csharp
using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.ProviderLogos.Commands.DeleteProviderLogo;

public sealed record DeleteProviderLogoCommand(string Key) : ICommand<ErrorOr<Success>>;
```

- [ ] **Step 2: Add validator**

Create `DeleteProviderLogoCommandValidator.cs`:

```csharp
using Chat.Application.ModelCatalog;

using FluentValidation;

namespace Chat.Application.ModelCatalog.ProviderLogos.Commands.DeleteProviderLogo;

internal sealed class DeleteProviderLogoCommandValidator : AbstractValidator<DeleteProviderLogoCommand>
{
    public DeleteProviderLogoCommandValidator()
    {
        RuleFor(x => x.Key)
            .NotEmpty()
            .MaximumLength(ModelCatalogLimits.ProviderLogoKeyMaxLength);
    }
}
```

- [ ] **Step 3: Add handler**

Create `DeleteProviderLogoHandler.cs`:

```csharp
using Chat.Application.Abstractions.ProviderLogos;

using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.ProviderLogos.Commands.DeleteProviderLogo;

internal sealed class DeleteProviderLogoHandler(IProviderLogoStorage storage)
    : ICommandHandler<DeleteProviderLogoCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle
    (
        DeleteProviderLogoCommand command,
        CancellationToken cancellationToken
    ) => await storage.DeleteAsync(command.Key, cancellationToken);
}
```

## Task 6: Implement S3 Provider Logo Storage

**Files:**
- Create: `src/services/Chat/Chat.Infrastructure/ProviderLogos/ProviderLogoStorageOptions.cs`
- Create: `src/services/Chat/Chat.Infrastructure/ProviderLogos/S3ProviderLogoStorage.cs`

- [ ] **Step 1: Add options**

Create `ProviderLogoStorageOptions.cs`:

```csharp
namespace Chat.Infrastructure.ProviderLogos;

internal sealed class ProviderLogoStorageOptions
{
    public const string SectionName = "ProviderLogoStorage";

    public required string BucketName { get; init; }

    public string Prefix { get; init; } = "providers/";

    public int PresignedUrlExpirationMinutes { get; init; } = 10;

    public Uri? PublicBaseUrl { get; init; }

    public string NormalizedPrefix => Prefix.Trim().Trim('/') + "/";
}
```

- [ ] **Step 2: Add S3 implementation**

Create `S3ProviderLogoStorage.cs`:

```csharp
using Amazon.S3;
using Amazon.S3.Model;

using Chat.Application.Abstractions.ProviderLogos;
using Chat.Application.ModelCatalog.ProviderLogos.Errors;

using ErrorOr;

using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.ProviderLogos;

internal sealed class S3ProviderLogoStorage
(
    IAmazonS3 s3,
    IOptions<ProviderLogoStorageOptions> options
) : IProviderLogoStorage
{
    private static readonly IReadOnlyDictionary<string, string> ExtensionsByContentType =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/svg+xml"] = "svg",
            ["image/png"] = "png",
            ["image/webp"] = "webp"
        };

    private readonly ProviderLogoStorageOptions _options = options.Value;

    public Task<ErrorOr<ProviderLogoUploadUrl>> CreateUploadUrlAsync
    (
        string providerSlug,
        string contentType,
        CancellationToken cancellationToken
    )
    {
        string normalizedContentType = contentType.Trim();

        if (!ExtensionsByContentType.TryGetValue(normalizedContentType, out string? extension))
        {
            return Task.FromResult<ErrorOr<ProviderLogoUploadUrl>>
            (
                ProviderLogoOperationErrors.UnsupportedContentType(contentType)
            );
        }

        string key = $"{_options.NormalizedPrefix}{providerSlug}/logo.{extension}";
        DateTime expiresAt = DateTime.UtcNow.AddMinutes(_options.PresignedUrlExpirationMinutes);

        GetPreSignedUrlRequest request = new()
        {
            BucketName = _options.BucketName,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = expiresAt,
            ContentType = normalizedContentType
        };

        string uploadUrl = s3.GetPreSignedURL(request);

        ProviderLogoUploadUrl result = new
        (
            UploadUrl: new Uri(uploadUrl),
            LogoKey: key,
            ExpiresAt: new DateTimeOffset(expiresAt, TimeSpan.Zero),
            Headers: new Dictionary<string, string>
            {
                ["Content-Type"] = normalizedContentType
            }
        );

        return Task.FromResult<ErrorOr<ProviderLogoUploadUrl>>(result);
    }

    public async Task<IReadOnlyCollection<ProviderLogoObject>> ListAsync(CancellationToken cancellationToken)
    {
        List<ProviderLogoObject> logos = [];
        string? continuationToken = null;

        do
        {
            ListObjectsV2Response response = await s3.ListObjectsV2Async
            (
                new ListObjectsV2Request
                {
                    BucketName = _options.BucketName,
                    Prefix = _options.NormalizedPrefix,
                    ContinuationToken = continuationToken
                },
                cancellationToken
            );

            logos.AddRange(response.S3Objects
                .Where(obj => !obj.Key.EndsWith('/', StringComparison.Ordinal))
                .Select(ToProviderLogoObject));

            continuationToken = response.IsTruncated.GetValueOrDefault()
                ? response.NextContinuationToken
                : null;
        } while (continuationToken is not null);

        return logos
            .OrderBy(logo => logo.Key, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<ErrorOr<Success>> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        string normalizedKey = key.Trim();

        if (!IsValidProviderLogoKey(normalizedKey))
        {
            return ProviderLogoOperationErrors.InvalidKey(key);
        }

        await s3.DeleteObjectAsync
        (
            new DeleteObjectRequest
            {
                BucketName = _options.BucketName,
                Key = normalizedKey
            },
            cancellationToken
        );

        return Result.Success;
    }

    private ProviderLogoObject ToProviderLogoObject(S3Object obj)
    {
        string fileName = Path.GetFileName(obj.Key);

        return new ProviderLogoObject
        (
            Key: obj.Key,
            FileName: fileName,
            ContentType: GetContentType(obj.Key),
            Size: obj.Size.GetValueOrDefault(),
            LastModified: obj.LastModified is null
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(obj.LastModified.Value, DateTimeKind.Utc)),
            PublicUrl: CreatePublicUrl(obj.Key)
        );
    }

    private bool IsValidProviderLogoKey(string key) =>
        key.StartsWith(_options.NormalizedPrefix, StringComparison.Ordinal)
        && key.Length > _options.NormalizedPrefix.Length
        && !key.Contains("..", StringComparison.Ordinal)
        && ExtensionsByContentType.Values.Any(extension =>
            key.EndsWith($".{extension}", StringComparison.OrdinalIgnoreCase));

    private static string GetContentType(string key)
    {
        string extension = Path.GetExtension(key).TrimStart('.');

        return ExtensionsByContentType.FirstOrDefault(x =>
                string.Equals(x.Value, extension, StringComparison.OrdinalIgnoreCase))
            .Key ?? "application/octet-stream";
    }

    private Uri? CreatePublicUrl(string key)
    {
        if (_options.PublicBaseUrl is null)
        {
            return null;
        }

        return new Uri(_options.PublicBaseUrl, key);
    }
}
```

If the selected `AWSSDK.S3` version exposes only an async presign API, keep the `Task<ErrorOr<ProviderLogoUploadUrl>>` method signature and replace `s3.GetPreSignedURL(request)` with the SDK's async equivalent inside this file only.

## Task 7: Register Provider Logo Storage

**Files:**
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Add using directives**

Add these usings:

```csharp
using Amazon.S3;

using Chat.Application.Abstractions.ProviderLogos;
using Chat.Infrastructure.ProviderLogos;
```

- [ ] **Step 2: Add storage registration to the infrastructure pipeline**

Change `AddInfrastructure` to include `.AddProviderLogoStorage(configuration)` before `.AddMessagingServices(configuration)`:

```csharp
public static IServiceCollection
    AddInfrastructure(this IServiceCollection services, IConfiguration configuration) =>
    services
        .AddSharedInfrastructure()
        .AddAuth0JwtAuthentication(configuration)
        .AddDatabaseServices()
        .AddCacheServices(configuration)
        .AddReaders()
        .AddProviderLogoStorage(configuration)
        .AddMessagingServices(configuration);
```

- [ ] **Step 3: Add the registration method**

Add this method inside `DependencyInjection`:

```csharp
private static IServiceCollection AddProviderLogoStorage
(
    this IServiceCollection services,
    IConfiguration configuration
)
{
    services
        .AddOptions<ProviderLogoStorageOptions>()
        .Bind(configuration.GetSection(ProviderLogoStorageOptions.SectionName))
        .Validate(options => !string.IsNullOrWhiteSpace(options.BucketName), "Provider logo bucket name is required.")
        .Validate(options => !string.IsNullOrWhiteSpace(options.Prefix), "Provider logo prefix is required.")
        .Validate(options => options.PresignedUrlExpirationMinutes > 0, "Provider logo upload URL expiration must be positive.")
        .ValidateOnStart();

    services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());
    services.AddScoped<IProviderLogoStorage, S3ProviderLogoStorage>();

    return services;
}
```

AWS credentials and region will come from the standard AWS SDK environment/configuration chain.

## Task 8: Add Upload URL Endpoint

**Files:**
- Create: `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/RequestProviderLogoUploadUrl/Request.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/RequestProviderLogoUploadUrl/Response.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/RequestProviderLogoUploadUrl/Endpoint.cs`

- [ ] **Step 1: Add request contract**

Create `Request.cs`:

```csharp
namespace Chat.Api.Endpoints.ModelCatalog.RequestProviderLogoUploadUrl;

internal sealed class Request
{
    public required string ContentType { get; init; }
}
```

- [ ] **Step 2: Add response contract**

Create `Response.cs`:

```csharp
namespace Chat.Api.Endpoints.ModelCatalog.RequestProviderLogoUploadUrl;

internal sealed class Response
{
    public required Uri UploadUrl { get; init; }

    public required string LogoKey { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public required IReadOnlyDictionary<string, string> Headers { get; init; }
}
```

- [ ] **Step 3: Add endpoint**

Create `Endpoint.cs`:

```csharp
using Chat.Api.Security;
using Chat.Application.Abstractions.ProviderLogos;
using Chat.Application.ModelCatalog.ProviderLogos.Commands.RequestProviderLogoUploadUrl;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Endpoints;

namespace Chat.Api.Endpoints.ModelCatalog.RequestProviderLogoUploadUrl;

internal sealed class Endpoint(ISender sender) : BaseEndpoint<Request, Response>
{
    public const string RouteName = "Chat.ModelCatalog.ProviderLogos.RequestUploadUrl";

    public override void Configure()
    {
        Post("/model-catalog/providers/{providerId}/logo-upload-url");
        Version(1);

        Permissions(ChatPermissions.ManageModelCatalog);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Request Provider Logo Upload URL")
                .WithDescription("Creates a presigned S3 PUT URL for uploading an LLM provider logo.")
                .Produces<Response>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.ModelCatalog);
        });
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        RequestProviderLogoUploadUrlCommand command = new
        (
            ProviderId: Route<Guid>("providerId"),
            ContentType: req.ContentType
        );

        ErrorOr<ProviderLogoUploadUrl> result = await sender.Send(command, ct);

        await SendErrorOrAsync
        (
            errorOr: result,
            mapper: ToResponse,
            cancellationToken: ct
        );
    }

    private static Response ToResponse(ProviderLogoUploadUrl uploadUrl) => new()
    {
        UploadUrl = uploadUrl.UploadUrl,
        LogoKey = uploadUrl.LogoKey,
        ExpiresAt = uploadUrl.ExpiresAt,
        Headers = uploadUrl.Headers
    };
}
```

## Task 9: Add List Provider Logos Endpoint

**Files:**
- Create: `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/ListProviderLogos/ProviderLogoResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/ListProviderLogos/Response.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/ListProviderLogos/Endpoint.cs`

- [ ] **Step 1: Add item response**

Create `ProviderLogoResponse.cs`:

```csharp
namespace Chat.Api.Endpoints.ModelCatalog.ListProviderLogos;

internal sealed class ProviderLogoResponse
{
    public required string Key { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required long Size { get; init; }

    public DateTimeOffset? LastModified { get; init; }

    public Uri? PublicUrl { get; init; }
}
```

- [ ] **Step 2: Add collection response**

Create `Response.cs`:

```csharp
namespace Chat.Api.Endpoints.ModelCatalog.ListProviderLogos;

internal sealed class Response
{
    public required IReadOnlyCollection<ProviderLogoResponse> Logos { get; init; }
}
```

- [ ] **Step 3: Add endpoint**

Create `Endpoint.cs`:

```csharp
using Chat.Api.Security;
using Chat.Application.Abstractions.ProviderLogos;
using Chat.Application.ModelCatalog.ProviderLogos.Queries.ListProviderLogos;

using FastEndpoints;

using Mediator;

namespace Chat.Api.Endpoints.ModelCatalog.ListProviderLogos;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest<Response>
{
    public const string RouteName = "Chat.ModelCatalog.ProviderLogos.List";

    public override void Configure()
    {
        Get("/model-catalog/provider-logos");
        Version(1);

        Permissions(ChatPermissions.ManageModelCatalog);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("List Provider Logos")
                .WithDescription("Lists provider logo objects stored under the configured S3 provider-logo prefix.")
                .Produces<Response>(StatusCodes.Status200OK, "application/json")
                .WithTags(CustomTags.ModelCatalog);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        IReadOnlyCollection<ProviderLogoObject> logos = await sender.Send(new ListProviderLogosQuery(), ct);

        await Send.ResponseAsync
        (
            new Response
            {
                Logos = logos.Select(ToResponse).ToArray()
            },
            cancellation: ct
        );
    }

    private static ProviderLogoResponse ToResponse(ProviderLogoObject logo) => new()
    {
        Key = logo.Key,
        FileName = logo.FileName,
        ContentType = logo.ContentType,
        Size = logo.Size,
        LastModified = logo.LastModified,
        PublicUrl = logo.PublicUrl
    };
}
```

## Task 10: Add Delete Provider Logo Endpoint

**Files:**
- Create: `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/DeleteProviderLogo/Endpoint.cs`

- [ ] **Step 1: Add endpoint**

Create `Endpoint.cs`:

```csharp
using Chat.Api.Security;
using Chat.Application.ModelCatalog.ProviderLogos.Commands.DeleteProviderLogo;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.ModelCatalog.DeleteProviderLogo;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest
{
    public const string RouteName = "Chat.ModelCatalog.ProviderLogos.Delete";

    public override void Configure()
    {
        Delete("/model-catalog/provider-logos");
        Version(1);

        Permissions(ChatPermissions.ManageModelCatalog);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Delete Provider Logo")
                .WithDescription("Deletes a provider logo object from the configured S3 provider-logo prefix.")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .WithTags(CustomTags.ModelCatalog);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        DeleteProviderLogoCommand command = new(Query<string>("key", isRequired: true));

        ErrorOr<Success> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.NoContentAsync(ct);
    }
}
```

If FastEndpoints reports a compile error for the `Query<string>("key", isRequired: true)` overload, replace that line with:

```csharp
string key = Query<string>("key");
DeleteProviderLogoCommand command = new(key);
```

and rely on `DeleteProviderLogoCommandValidator` to reject missing or empty keys.

## Task 11: Verify Build

**Files:**
- No source files created or modified in this task.

- [ ] **Step 1: Check worktree**

Run:

```bash
git status --short
```

Expected: the files from this plan are modified or untracked. There may also be the already-created design and plan files.

- [ ] **Step 2: Restore packages**

Run with elevated permission because `AGENTS.md` requires escalation for `dotnet restore`:

```bash
dotnet restore Nova.slnx
```

Expected: restore completes successfully and downloads `AWSSDK.S3` if it is not already cached.

- [ ] **Step 3: Build solution**

Run with elevated permission because `AGENTS.md` requires escalation for `dotnet build`:

```bash
dotnet build Nova.slnx --no-restore
```

Expected: build succeeds. If `AWSSDK.S3` v4 has presign API differences, fix only `S3ProviderLogoStorage.cs` and rerun the build.

- [ ] **Step 4: Report verification**

Report the exact restore/build result to the user. Do not claim tests pass unless the user explicitly asked for test work and tests were run.

## Manual Smoke Check After Deployment

Use a real manager token and a real provider ID.

- Request upload URL:

```http
POST /model-catalog/providers/{providerId}/logo-upload-url
Content-Type: application/json

{
  "contentType": "image/svg+xml"
}
```

Expected response includes `logoKey` like `providers/openai/logo.svg`, `headers.Content-Type = image/svg+xml`, and a presigned `uploadUrl`.

- Upload directly to S3 using the returned URL and headers.
- Call existing provider update with the returned `logoKey`.
- Call `GET /model-catalog/provider-logos` and verify the uploaded key appears.
- Call `DELETE /model-catalog/provider-logos?key=providers/openai/logo.svg` and verify it returns `204`.

## Self-Review

- Spec coverage:
  - Presigned upload URL: Task 3, Task 6, Task 8.
  - Derived provider slug naming under `providers/`: Task 3 and Task 6.
  - List only provider prefix: Task 4, Task 6, Task 9.
  - Delete only provider prefix: Task 5, Task 6, Task 10.
  - Domain unchanged: no domain files are modified in this plan.
  - Existing provider update remains owner of `LogoKey`: no automatic provider mutation is added.
  - S3 infrastructure and configuration: Task 1, Task 6, Task 7.
  - FastEndpoints and Mediator conventions: Task 3 through Task 10.
- Placeholder scan: no unfinished-work markers are intentionally left.
- Type consistency:
  - `ProviderLogoUploadUrl`, `ProviderLogoObject`, and `IProviderLogoStorage` are defined before use.
  - Command/query result types match handler and endpoint usage.
  - Error names and namespaces match across application and infrastructure.
