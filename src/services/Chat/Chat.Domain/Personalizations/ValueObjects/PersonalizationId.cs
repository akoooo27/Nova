using ErrorOr;

namespace Chat.Domain.Personalizations.ValueObjects;

public sealed record PersonalizationId
{
    public Guid Value { get; }

    private PersonalizationId(Guid value)
    {
        Value = value;
    }

    public static PersonalizationId New() => new(Guid.CreateVersion7());

    public static ErrorOr<PersonalizationId> Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            return Error.Validation
            (
                code: "PersonalizationId.Empty",
                description: "Personalization id cannot be empty."
            );
        }

        return new PersonalizationId(value);
    }

    public static PersonalizationId FromDatabase(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("Database contained an empty chat id.");

        return new PersonalizationId(value);
    }

    public override string ToString() => Value.ToString();
}