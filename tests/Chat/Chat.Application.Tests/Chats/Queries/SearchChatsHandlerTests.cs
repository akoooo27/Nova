using Chat.Application.Chats.Queries.SearchChats;
using Chat.Application.Tests.FavoriteModels;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Chats.Queries;

public sealed class SearchChatsHandlerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleSearchesForCurrentUser(bool isArchived)
    {
        UserId userId = UserId.FromDatabase("auth0|user-1");
        ChatSearchReadModel readModel = new(Chats: [], Total: 0, Limit: 20, Offset: 0);
        FakeChatSearchReader reader = new(readModel);
        SearchChatsHandler handler = new
        (
            userContext: new FakeUserContext(userId.Value),
            reader: reader
        );

        ErrorOr<ChatSearchReadModel> result = await handler.Handle
        (
            new SearchChatsQuery(Query: "  memory bug  ", IsArchived: isArchived, Limit: 20, Offset: 0),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Same(readModel, result.Value);
        Assert.Equal(userId, reader.RequestedUserId);
        Assert.Equal("memory bug", reader.RequestedQuery);
        Assert.Equal(isArchived, reader.RequestedIsArchived);
        Assert.Equal(20, reader.RequestedLimit);
        Assert.Equal(0, reader.RequestedOffset);
        Assert.Equal(1, reader.SearchCallCount);
    }

    [Fact]
    public async Task HandleReturnsErrorAndSkipsReaderWhenUserIdMissing()
    {
        ChatSearchReadModel readModel = new(Chats: [], Total: 0, Limit: 20, Offset: 0);
        FakeChatSearchReader reader = new(readModel);
        SearchChatsHandler handler = new
        (
            userContext: new FakeUserContext(string.Empty),
            reader: reader
        );

        ErrorOr<ChatSearchReadModel> result = await handler.Handle
        (
            new SearchChatsQuery(Query: "memory bug", IsArchived: false, Limit: 20, Offset: 0),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal(0, reader.SearchCallCount);
    }
}