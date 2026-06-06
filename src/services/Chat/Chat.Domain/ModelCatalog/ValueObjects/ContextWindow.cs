using System.Globalization;

using ErrorOr;

namespace Chat.Domain.ModelCatalog.ValueObjects;

public sealed record ContextWindow
{
    public int Value { get; }

    private ContextWindow(int value)
    {
        Value = value;
    }

    public static ErrorOr<ContextWindow> Create(int value)
    {
        if (value < 1)
        {
            return Error.Validation
            (
                code: "ContextWindow.Invalid",
                description: "Context window must be greater than or equal to 1."
            );
        }

        return new ContextWindow(value);
    }

    public static ContextWindow FromDatabase(int value)
    {
        if (value < 1)
            throw new DomainException("Database contained an invalid context window.");

        return new ContextWindow(value);
    }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}