using ErrorOr;

namespace Chat.Domain.SharedChats.ValueObjects;

public sealed record SharedChatId
{
    public Guid Value { get; }

    private SharedChatId(Guid value)
    {
        Value = value;
    }

    // Random UUIDv4 (not v7): this id is an anonymous bearer credential for the public
    // share link, so it must be unguessable and must not leak the chat's creation time.
    public static SharedChatId New() => new(Guid.NewGuid());

    public static ErrorOr<SharedChatId> Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            return Error.Validation
            (
                code: "SharedChatId.Empty",
                description: "Shared chat id cannot be empty."
            );
        }

        return new SharedChatId(value);
    }

    public static SharedChatId FromDatabase(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("Database contained an empty shared chat id.");

        return new SharedChatId(value);
    }

    public override string ToString() => Value.ToString();
}