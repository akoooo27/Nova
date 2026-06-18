using ErrorOr;

namespace Chat.Domain.Chats.ValueObjects;

public sealed record MessageContent
{
    public const int MaxLength = 32_768;

    public string Value { get; }

    private MessageContent(string value)
    {
        Value = value;
    }

    public static ErrorOr<MessageContent> Create(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "MessageContent.Required",
                description: "Message content is required."
            );
        }

        if (trimmed.Length > MaxLength)
        {
            return Error.Validation
            (
                code: "MessageContent.TooLong",
                description: $"Message content cannot exceed {MaxLength} characters."
            );
        }

        return new MessageContent(trimmed);
    }

    public static MessageContent FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
            throw new DomainException("Database contained invalid message content.");

        return new MessageContent(value);
    }

    public override string ToString() => Value;
}