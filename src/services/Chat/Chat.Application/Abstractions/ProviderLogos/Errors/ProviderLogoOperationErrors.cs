using ErrorOr;

namespace Chat.Application.Abstractions.ProviderLogos.Errors;

public static class ProviderLogoOperationErrors
{
    public static Error UnsupportedContentType(string contentType) =>
        Error.Validation
        (
            code: "ProviderLogo.UnsupportedContentType",
            description:
            $"Provider logos must be uploaded as image/svg+xml, image/png, or image/webp. Received '{contentType}'."
        );

    public static Error InvalidKey(string key) =>
        Error.Validation
        (
            code: "ProviderLogo.InvalidKey",
            description: $"Provider logo key '{key}' is invalid."
        );
}