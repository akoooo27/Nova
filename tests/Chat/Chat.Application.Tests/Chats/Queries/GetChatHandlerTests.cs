using Chat.Application.Chats.Queries.GetChat;
using Chat.Application.Tests.FavoriteModels;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Chats.Queries;

public sealed class GetChatHandlerTests
{
    [Fact]
    public async Task HandleReturnsChatForOwner()
    {
        UserId userId = UserId.FromDatabase("auth0|user-1");
        Guid chatId = Guid.CreateVersion7();
        ChatDetailReadModel readModel = new
        (
            Id: chatId,
            Title: "ACCA F3",
            IsPinned: false,
            PinnedAt: null,
            IsArchived: false,
            IsTemporary: false,
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt: DateTimeOffset.UtcNow,
            CurrentMessageId: Guid.CreateVersion7(),
            Messages: []
        );
        FakeChatDetailReader reader = new(readModel);
        GetChatHandler handler = new
        (
            userContext: new FakeUserContext(userId.Value),
            reader: reader
        );

        ErrorOr<ChatDetailReadModel> result = await handler.Handle
        (
            new GetChatQuery(chatId),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Same(readModel, result.Value);
        Assert.Equal(chatId, reader.RequestedChatId!.Value);
        Assert.Equal(userId, reader.RequestedUserId);
        Assert.Equal(1, reader.GetCallCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWhenReaderReturnsNull()
    {
        FakeChatDetailReader reader = new(readModel: null);
        GetChatHandler handler = new
        (
            userContext: new FakeUserContext("auth0|user-1"),
            reader: reader
        );

        ErrorOr<ChatDetailReadModel> result = await handler.Handle
        (
            new GetChatQuery(Guid.CreateVersion7()),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal("Chat.NotFound", result.FirstError.Code);
    }
}