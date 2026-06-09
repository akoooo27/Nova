using ErrorOr;

namespace Chat.Domain.Chats.ValueObjects;

public sealed record ChatId
{
    public Guid Value { get; }

    private ChatId(Guid value)
    {
        Value = value;
    }

    public static ChatId New() => new(Guid.CreateVersion7());

    public static ErrorOr<ChatId> Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            return Error.Validation
            (
                code: "ChatId.Empty",
                description: "Chat id cannot be empty."
            );
        }

        return new ChatId(value);
    }

    public static ChatId FromDatabase(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("Database contained an empty chat id.");

        return new ChatId(value);
    }

    public override string ToString() => Value.ToString();
}