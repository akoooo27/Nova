using ErrorOr;

namespace Chat.Domain.Chats.ValueObjects;

public sealed record ChatMessageId
{
    public Guid Value { get; }

    private ChatMessageId(Guid value)
    {
        Value = value;
    }

    public static ChatMessageId New() => new(Guid.CreateVersion7());

    public static ErrorOr<ChatMessageId> Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            return Error.Validation
            (
                code: "ChatMessageId.Empty",
                description: "Chat message id cannot be empty."
            );
        }

        return new ChatMessageId(value);
    }

    public static ChatMessageId FromDatabase(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("Database contained an empty chat message id.");

        return new ChatMessageId(value);
    }

    public override string ToString() => Value.ToString();
}