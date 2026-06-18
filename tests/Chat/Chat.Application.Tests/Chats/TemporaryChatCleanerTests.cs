using Chat.Application.Chats.Cleanup;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Tests.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Application.Tests.Chats;

public sealed class TemporaryChatCleanerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(30);

    [Fact]
    public async Task DeleteExpiredAsyncDeletesOnlyTemporaryThreadsOlderThanRetentionPeriod()
    {
        FakeChatRepository chats = new();
        TemporaryChatCleaner cleaner = new(chats, new FakeDateTimeProvider(Now));

        ChatThread expiredTemporary = CreateThread(isTemporary: true, createdAt: Now.AddDays(-31));
        ChatThread freshTemporary = CreateThread(isTemporary: true, createdAt: Now.AddDays(-29));
        ChatThread oldPermanent = CreateThread(isTemporary: false, createdAt: Now.AddDays(-31));

        chats.Seed(expiredTemporary);
        chats.Seed(freshTemporary);
        chats.Seed(oldPermanent);

        int deleted = await cleaner.DeleteExpiredAsync(RetentionPeriod, CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.DoesNotContain(expiredTemporary, chats.Threads);
        Assert.Contains(freshTemporary, chats.Threads);
        Assert.Contains(oldPermanent, chats.Threads);
    }

    private static ChatThread CreateThread(bool isTemporary, DateTimeOffset createdAt)
    {
        return ChatThread.Create
        (
            userId: UserId.FromDatabase("auth0|user-1"),
            title: ChatTitle.FromDatabase("Planning chat"),
            firstUserMessage: MessageContent.Create("Hello").Value,
            createdAt: createdAt,
            isTemporary: isTemporary
        );
    }
}