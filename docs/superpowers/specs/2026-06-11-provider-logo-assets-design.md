# Provider Logo Assets Design

## Goal

Allow model catalog managers to upload, list, and delete LLM provider logo files in the public assets S3 bucket. Logo files are stored only under the `providers/` prefix in `nova-public-assets-dev`, and object keys are derived by the backend rather than chosen by users.

## Boundary

Provider logo file management is an application/API feature backed by infrastructure storage. It is not model catalog domain behavior.

The model catalog domain continues to treat `LlmProvider.LogoKey` as an opaque asset key. Existing provider create and update operations remain responsible for setting or clearing the provider's `LogoKey`. S3 upload and delete operations do not directly mutate provider aggregates.

This keeps storage concerns in infrastructure while still exposing the workflow through model catalog manager endpoints, permissions, and API tags.

## HTTP Operations

FastEndpoints exposes these manager-only operations:

```http
POST   /model-catalog/providers/{providerId}/logo-upload-url
GET    /model-catalog/provider-logos
DELETE /model-catalog/provider-logos?key=providers/openai/logo.svg
```

All operations require the existing `chat:model-catalog:manage` permission.

### Request Upload URL

`POST /model-catalog/providers/{providerId}/logo-upload-url` creates a short-lived presigned S3 `PUT` URL for an existing provider.

The request includes the intended content type. Supported content types are:

- `image/svg+xml`
- `image/png`
- `image/webp`

The handler loads the provider by `providerId`. A missing provider returns `404 Not Found`. The object key is derived from the provider's current slug and content type:

```text
providers/{provider-slug}/logo.svg
providers/{provider-slug}/logo.png
providers/{provider-slug}/logo.webp
```

The response includes:

```text
UploadUrl
LogoKey
ExpiresAt
Headers
```

`LogoKey` is the S3 object key that should be passed to the existing provider update endpoint after the frontend uploads the file. `Headers` contains any required upload headers, including the expected `Content-Type`.

The upload URL expiration is configurable and defaults to 10 minutes.

### List Provider Logos

`GET /model-catalog/provider-logos` lists existing objects under the configured provider-logo prefix.

Each listed object includes:

```text
Key
FileName
ContentType
Size
LastModified
PublicUrl
```

`PublicUrl` is included only when a CDN or public asset base URL is configured. The list operation never returns objects outside the configured prefix.

### Delete Provider Logo

`DELETE /model-catalog/provider-logos?key=...` deletes an object from S3.

The requested key must start with the configured provider-logo prefix. Invalid keys return `400 Bad Request`. Deleting a missing object is treated as an idempotent success and returns `204 No Content`.

Deleting an S3 object does not automatically clear any provider's `LogoKey`. If a catalog manager deletes the logo currently referenced by a provider, the frontend should also call the existing provider update endpoint with `LogoKey = null` or with a replacement key.

## Naming And Safety

Users cannot supply arbitrary upload object keys. The upload operation derives keys from the provider slug controlled by catalog management. This prevents path traversal, accidental writes outside `providers/`, and inconsistent logo naming.

List and delete operations validate the configured prefix before calling S3. The configured prefix should normalize to a trailing slash, for example `providers/`.

Uploading a new logo with the same provider slug and file extension overwrites the previous object. This is intentional because provider logos are replacement assets, not versioned media records.

## Components

- `IProviderLogoStorage` application abstraction for presigning, listing, and deleting provider logo objects.
- Provider logo storage options for bucket, prefix, presigned URL expiration, and optional public asset base URL.
- S3-backed infrastructure implementation using the AWS S3 SDK.
- Mediator commands and query for requesting upload URLs, listing logos, and deleting logos.
- FastEndpoints endpoints under the model catalog API surface.

The implementation follows the existing `Mediator.SourceGenerator` and FastEndpoints conventions in the Chat service.

## Configuration

The Chat API reads provider logo storage settings from configuration:

```text
ProviderLogoStorage__BucketName = nova-public-assets-dev
ProviderLogoStorage__Prefix = providers/
ProviderLogoStorage__PresignedUrlExpirationMinutes = 10
ProviderLogoStorage__PublicBaseUrl = optional CDN/public asset base URL
```

AWS credentials and region use the standard AWS SDK configuration chain for the hosting environment.

## Error Handling

- Invalid provider IDs or malformed route values return validation errors through the existing API behavior.
- Missing providers return `404 Not Found`.
- Unsupported content types return `400 Bad Request`.
- Delete keys outside the provider-logo prefix return `400 Bad Request`.
- Missing objects on delete return `204 No Content`.
- Unexpected AWS failures propagate to the existing global exception handling.

## Scope

This design covers backend APIs, application abstractions, infrastructure storage integration, and configuration.

It does not add frontend implementation, automatic provider `LogoKey` mutation after upload or delete, image transformation, virus scanning, object versioning, or new database tables.

Tests are not included unless explicitly requested. Verification will use build checks, and `dotnet` commands require elevated permission under the project instructions.
