using ErrorOr;

namespace Chat.Domain.ModelCatalog.ValueObjects;

public sealed record ProviderSlug
{
    public string Value { get; }

    private ProviderSlug(string value)
    {
        Value = value;
    }

    public static ErrorOr<ProviderSlug> Create(string? value)
    {
        string? normalized = value?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Error.Validation
            (
                code: "ProviderSlug.Required",
                description: "Provider slug is required."
            );
        }

        if (!IsUrlSafeSlug(normalized))
        {
            return Error.Validation
            (
                code: "ProviderSlug.Invalid",
                description: "Provider slug must contain only lowercase letters, numbers, and hyphens."
            );
        }

        return new ProviderSlug(normalized);
    }

    public static ProviderSlug FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !IsUrlSafeSlug(value))
            throw new DomainException("Database contained an invalid provider slug.");

        return new ProviderSlug(value);
    }

    public override string ToString() => Value;

    private static bool IsUrlSafeSlug(string value)
    {
        if (value[0] == '-' || value[^1] == '-')
            return false;

        foreach (char character in value)
        {
            bool isLowercaseLetter = character is >= 'a' and <= 'z';
            bool isNumber = character is >= '0' and <= '9';

            if (!isLowercaseLetter && !isNumber && character != '-')
                return false;
        }

        return true;
    }
}