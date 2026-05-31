using ErrorOr;

namespace Chat.Domain.ModelCatalog.ValueObjects;

public sealed record ModelName
{
    public string Value { get; }

    private ModelName(string value)
    {
        Value = value;
    }

    public static ErrorOr<ModelName> Create(string? value)
    {
        string? trimmed = value?.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "ModelName.Required",
                description: "Model name is required."
            );
        }

        return new ModelName(trimmed);
    }

    public static ModelName FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
            throw new DomainException("Database contained an invalid model name.");

        return new ModelName(value);
    }

    public override string ToString() => Value;
}