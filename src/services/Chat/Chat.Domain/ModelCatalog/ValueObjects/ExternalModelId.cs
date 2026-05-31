using ErrorOr;

namespace Chat.Domain.ModelCatalog.ValueObjects;

public sealed record ExternalModelId
{
    public string Value { get; }

    private ExternalModelId(string value)
    {
        Value = value;
    }

    public static ErrorOr<ExternalModelId> Create(string? value)
    {
        string? trimmed = value?.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "ExternalModelId.Required",
                description: "External model id is required."
            );
        }

        return new ExternalModelId(trimmed);
    }

    public static ExternalModelId FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
            throw new DomainException("Database contained an invalid external model id.");

        return new ExternalModelId(value);
    }

    public override string ToString() => Value;
}