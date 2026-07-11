using Chat.Application.SharedChats.Queries.GetPublicSharedChat;
using Chat.Application.Tests.FavoriteModels;
using Chat.Domain.Chats.ValueObjects;

using ErrorOr;

namespace Chat.Application.Tests.SharedChats;

public sealed class GetPublicSharedChatHandlerTests
{
    [Fact]
    public async Task HandleReturnsSharedChatForAuthenticatedUser()
    {
        Guid sharedChatId = Guid.NewGuid();
        PublicSharedChatReadModel readModel = new
        (
            Id: sharedChatId,
            Title: "Shared chat",
            CreatedAt: DateTimeOffset.UtcNow,
            CurrentMessageId: Guid.CreateVersion7(),
            AllowRemix: false,
            Messages: []
        );
        FakePublicSharedChatReader reader = new(readModel);
        GetPublicSharedChatHandler handler = new
        (
            userContext: new FakeUserContext("auth0|user-1"),
            reader: reader
        );

        ErrorOr<PublicSharedChatReadModel> result = await handler.Handle
        (
            new GetPublicSharedChatQuery(sharedChatId),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Same(readModel, result.Value);
        Assert.Equal(sharedChatId, reader.SharedChatId!.Value);
        Assert.Equal(1, reader.CallCount);
    }

    [Fact]
    public async Task HandleReturnsAllowRemixFromReader()
    {
        Guid sharedChatId = Guid.NewGuid();
        PublicSharedChatReadModel readModel = new
        (
            Id: sharedChatId,
            Title: "Shared chat",
            CreatedAt: DateTimeOffset.UtcNow,
            CurrentMessageId: Guid.CreateVersion7(),
            AllowRemix: true,
            Messages: []
        );
        FakePublicSharedChatReader reader = new(readModel);
        GetPublicSharedChatHandler handler = new
        (
            userContext: new FakeUserContext("auth0|user-1"),
            reader: reader
        );

        ErrorOr<PublicSharedChatReadModel> result = await handler.Handle
        (
            new GetPublicSharedChatQuery(sharedChatId),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.True(result.Value.AllowRemix);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWhenSharedChatDoesNotExist()
    {
        FakePublicSharedChatReader reader = new(result: null);
        GetPublicSharedChatHandler handler = new
        (
            userContext: new FakeUserContext("auth0|user-1"),
            reader: reader
        );

        ErrorOr<PublicSharedChatReadModel> result = await handler.Handle
        (
            new GetPublicSharedChatQuery(Guid.NewGuid()),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal("SharedChatId.NotFound", result.FirstError.Code);
        Assert.Equal(1, reader.CallCount);
    }

    [Theory]
    [InlineData("", "UserId.Required")]
    [InlineData("auth0|user-1", "SharedChatId.Empty")]
    public async Task HandleReturnsValidationErrorAndSkipsReaderForInvalidInput
    (
        string userId,
        string expectedCode
    )
    {
        FakePublicSharedChatReader reader = new(result: null);
        GetPublicSharedChatHandler handler = new
        (
            userContext: new FakeUserContext(userId),
            reader: reader
        );

        ErrorOr<PublicSharedChatReadModel> result = await handler.Handle
        (
            new GetPublicSharedChatQuery(Guid.Empty),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, error => error.Code == expectedCode);
        Assert.Equal(0, reader.CallCount);
    }
}