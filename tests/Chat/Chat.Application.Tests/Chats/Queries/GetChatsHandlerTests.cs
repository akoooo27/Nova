using Chat.Application.Chats.Queries.GetChats;
using Chat.Application.Tests.FavoriteModels;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Chats.Queries;

public sealed class GetChatsHandlerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleReadsChatsForCurrentUserWithFiltersAndPaging(bool isArchived)
    {
        UserId userId = UserId.FromDatabase("auth0|user-1");
        ChatListReadModel readModel = new
        (
            Chats:
            [
                new ChatSummaryReadModel
                (
                    Id: Guid.CreateVersion7(),
                    Title: "Management chapter #17",
                    IsPinned: false,
                    PinnedAt: null,
                    IsArchived: false,
                    IsTemporary: false,
                    CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
                    UpdatedAt: DateTimeOffset.UtcNow
                )
            ],
            Total: 1,
            Limit: 20,
            Offset: 0
        );
        FakeChatListReader reader = new(readModel);
        GetChatsHandler handler = new
        (
            userContext: new FakeUserContext(userId.Value),
            reader: reader
        );

        ErrorOr<ChatListReadModel> result = await handler.Handle
        (
            new GetChatsQuery(IsArchived: isArchived, Limit: 20, Offset: 0),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Same(readModel, result.Value);
        Assert.Equal(userId, reader.RequestedUserId);
        Assert.Equal(isArchived, reader.RequestedIsArchived);
        Assert.Equal(20, reader.RequestedLimit);
        Assert.Equal(0, reader.RequestedOffset);
        Assert.Equal(1, reader.GetCallCount);
    }

    [Fact]
    public async Task HandleReturnsErrorAndSkipsReaderWhenUserIdMissing()
    {
        ChatListReadModel readModel = new([], Total: 0, Limit: 20, Offset: 0);
        FakeChatListReader reader = new(readModel);
        GetChatsHandler handler = new
        (
            userContext: new FakeUserContext(string.Empty),
            reader: reader
        );

        ErrorOr<ChatListReadModel> result = await handler.Handle
        (
            new GetChatsQuery(IsArchived: false, Limit: 20, Offset: 0),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal(0, reader.GetCallCount);
    }
}