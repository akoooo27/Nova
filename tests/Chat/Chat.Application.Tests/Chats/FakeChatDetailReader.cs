using Chat.Application.Chats.Queries.GetChat;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Application.Tests.Chats;

internal sealed class FakeChatDetailReader(ChatDetailReadModel? readModel) : IChatDetailReader
{
    public ChatId? RequestedChatId { get; private set; }

    public UserId? RequestedUserId { get; private set; }

    public int GetCallCount { get; private set; }

    public Task<ChatDetailReadModel?> GetAsync
    (
        ChatId chatId,
        UserId userId,
        CancellationToken cancellationToken
    )
    {
        RequestedChatId = chatId;
        RequestedUserId = userId;
        GetCallCount++;

        return Task.FromResult(readModel);
    }
}