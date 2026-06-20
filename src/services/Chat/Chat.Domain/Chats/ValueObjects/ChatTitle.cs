using ErrorOr;

namespace Chat.Domain.Chats.ValueObjects;

public sealed record ChatTitle
{
    public const int MaxLength = 200;

    public string Value { get; }

    private ChatTitle(string value)
    {
        Value = value;
    }

    public static ErrorOr<ChatTitle> Create(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "ChatTitle.Required",
                description: "Chat title is required."
            );
        }

        if (trimmed.Length > MaxLength)
        {
            return Error.Validation
            (
                code: "ChatTitle.TooLong",
                description: $"Chat title cannot exceed {MaxLength} characters."
            );
        }

        return new ChatTitle(trimmed);
    }

    public static ChatTitle CreateBranch(ChatTitle source)
    {
        const string prefix = "Branch: ";

        int sourceLength = Math.Min(source.Value.Length, MaxLength - prefix.Length);

        return new ChatTitle($"{prefix}{source.Value[..sourceLength]}");
    }

    public static ChatTitle FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
            throw new DomainException("Database contained an invalid chat title.");

        return new ChatTitle(value);
    }

    public override string ToString() => Value;
}