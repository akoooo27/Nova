using ErrorOr;

namespace Chat.Domain.ModelCatalog.ValueObjects;

public sealed record LlmProviderId
{
    public Guid Value { get; }

    private LlmProviderId(Guid value)
    {
        Value = value;
    }

    public static LlmProviderId New() => new(Guid.CreateVersion7());

    public static ErrorOr<LlmProviderId> Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            return Error.Validation
            (
                code: "LlmProviderId.Empty",
                description: "Llm provider id cannot be empty."
            );
        }

        return new LlmProviderId(value);
    }

    public static LlmProviderId FromDatabase(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("Database contained an empty llm provider id.");

        return new LlmProviderId(value);
    }

    public override string ToString() => Value.ToString();
}