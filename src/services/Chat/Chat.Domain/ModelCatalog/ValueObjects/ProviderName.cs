using ErrorOr;

namespace Chat.Domain.ModelCatalog.ValueObjects;

public sealed record ProviderName
{
    public string Value { get; }

    private ProviderName(string value)
    {
        Value = value;
    }

    public static ErrorOr<ProviderName> Create(string? value)
    {
        string? trimmed = value?.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "ProviderName.Required",
                description: "Provider name is required."
            );
        }

        return new ProviderName(trimmed);
    }

    public static ProviderName FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
            throw new DomainException("Database contained an invalid provider name.");

        return new ProviderName(value);
    }

    public override string ToString() => Value;
}