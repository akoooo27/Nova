namespace Chat.Domain.Chats.ValueObjects;

public sealed record ChatBranchOrigin
{
    public ChatId SourceChatId { get; private init; } = default!;

    public ChatMessageId SourceMessageId { get; private init; } = default!;

    private ChatBranchOrigin()
    {
        // EF Core materialization only
    }

    private ChatBranchOrigin(ChatId sourceChatId, ChatMessageId sourceMessageId)
    {
        SourceChatId = sourceChatId;
        SourceMessageId = sourceMessageId;
    }

    internal static ChatBranchOrigin Create(ChatId sourceChatId, ChatMessageId sourceMessageId) =>
        new(sourceChatId, sourceMessageId);
}