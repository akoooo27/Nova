using Chat.Domain.SharedChats.ValueObjects;

namespace Chat.Domain.Chats.ValueObjects;

public sealed record ChatRemixOrigin
{
    public SharedChatId ShareId { get; private init; } = default!;

    public ChatId SourceChatId { get; private init; } = default!;

    public ChatMessageId SourceMessageId { get; private init; } = default!;

    private ChatRemixOrigin()
    {
        // EF Core materialization only
    }

    private ChatRemixOrigin
    (
        SharedChatId shareId,
        ChatId sourceChatId,
        ChatMessageId sourceMessageId
    )
    {
        ShareId = shareId;
        SourceChatId = sourceChatId;
        SourceMessageId = sourceMessageId;
    }

    internal static ChatRemixOrigin Create
    (
        SharedChatId shareId,
        ChatId sourceChatId,
        ChatMessageId sourceMessageId
    ) => new
    (
        shareId: shareId,
        sourceChatId: sourceChatId,
        sourceMessageId: sourceMessageId
    );
}