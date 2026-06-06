using ErrorOr;

namespace Chat.Domain.ModelCatalog.ValueObjects;

public sealed record ModelDescription
{
    public string Value { get; }

    private ModelDescription(string value)
    {
        Value = value;
    }

    public static ErrorOr<ModelDescription> Create(string? value)
    {
        string? trimmed = value?.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "ModelDescription.Required",
                description: "Model description is required."
            );
        }

        return new ModelDescription(trimmed);
    }

    public static ModelDescription FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
            throw new DomainException("Database contained an invalid model description.");

        return new ModelDescription(value);
    }

    public override string ToString() => Value;
}