using ErrorOr;

namespace Chat.Domain.ModelCatalog.ValueObjects;

public sealed record LlmModelId
{
    public Guid Value { get; }

    private LlmModelId(Guid value)
    {
        Value = value;
    }

    public static LlmModelId New() => new(Guid.CreateVersion7());

    public static ErrorOr<LlmModelId> Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            return Error.Validation
            (
                code: "LlmModelId.Empty",
                description: "Llm model id cannot be empty."
            );
        }

        return new LlmModelId(value);
    }

    public static LlmModelId FromDatabase(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("Database contained an empty llm model id.");

        return new LlmModelId(value);
    }

    public override string ToString() => Value.ToString();
}