namespace Chat.Domain.Chats.ValueObjects;

public sealed record SiblingIndex
{
    public int Value { get; }

    private SiblingIndex(int value)
    {
        Value = value;
    }

    public static SiblingIndex First() => new(0);

    public static SiblingIndex Next(int existingCount)
    {
        if (existingCount < 0)
            throw new DomainException("Sibling count cannot be negative.");

        return new SiblingIndex(existingCount);
    }

    public static SiblingIndex FromDatabase(int value)
    {
        if (value < 0)
            throw new DomainException("Database contained an invalid sibling index.");

        return new SiblingIndex(value);
    }
}