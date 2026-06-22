using Chat.Application.SharedChats.Queries.GetSharedChats;
using Chat.Application.Tests.FavoriteModels;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.SharedChats;

public sealed class GetSharedChatsHandlerTests
{
    [Fact]
    public async Task HandleReadsCurrentUsersSharedChatsWithPaging()
    {
        SharedChatListReadModel readModel = new
        (
            SharedChats:
            [
                new SharedChatSummaryReadModel
                (
                    Id: Guid.NewGuid(),
                    Title: "Shared chat",
                    ChatId: Guid.CreateVersion7(),
                    CurrentMessageId: Guid.CreateVersion7(),
                    CreatedAt: DateTimeOffset.UtcNow
                )
            ],
            Total: 1,
            Limit: 50,
            Offset: 25
        );
        FakeSharedChatListReader reader = new(readModel);
        GetSharedChatsHandler handler = new
        (
            userContext: new FakeUserContext("auth0|user-1"),
            reader: reader
        );

        ErrorOr<SharedChatListReadModel> result = await handler.Handle
        (
            new GetSharedChatsQuery(Limit: 50, Offset: 25),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Same(readModel, result.Value);
        Assert.Equal("auth0|user-1", reader.UserId!.Value);
        Assert.Equal(50, reader.Limit);
        Assert.Equal(25, reader.Offset);
        Assert.Equal(1, reader.CallCount);
    }

    [Fact]
    public async Task HandleReturnsValidationErrorAndSkipsReaderWhenUserIdMissing()
    {
        FakeSharedChatListReader reader = new(new([], Total: 0, Limit: 50, Offset: 0));
        GetSharedChatsHandler handler = new
        (
            userContext: new FakeUserContext(string.Empty),
            reader: reader
        );

        ErrorOr<SharedChatListReadModel> result = await handler.Handle
        (
            new GetSharedChatsQuery(Limit: 50, Offset: 0),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal(0, reader.CallCount);
    }
}