using System.Globalization;

using ErrorOr;

namespace Chat.Domain.ModelCatalog.ValueObjects;

public sealed record SortOrder
{
    public int Value { get; }

    private SortOrder(int value)
    {
        Value = value;
    }

    public static SortOrder First => new(1);

    public static ErrorOr<SortOrder> Create(int value)
    {
        if (value < 1)
        {
            return Error.Validation
            (
                code: "SortOrder.Invalid",
                description: "Sort order must be greater than or equal to 1."
            );
        }

        return new SortOrder(value);
    }

    public static SortOrder FromDatabase(int value)
    {
        if (value < 1)
            throw new DomainException("Database contained an invalid sort order.");

        return new SortOrder(value);
    }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}