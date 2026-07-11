using Chat.Application.Chats.Commands.DeleteChat;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Tests.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Chats.Commands;

public sealed class DeleteChatHandlerTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeChatRepository _chats = new();
    private readonly TurnFakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task HandleDeletesOwnedChat()
    {
        ChatThread thread = CreateThread();
        _chats.Seed(thread);

        ErrorOr<Deleted> result = await CreateHandler().Handle
        (
            new DeleteChatCommand(thread.Id.Value),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Empty(_chats.Threads);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWhenChatDoesNotBelongToUser()
    {
        ChatThread thread = CreateThread(userId: "auth0|other-user");
        _chats.Seed(thread);

        ErrorOr<Deleted> result = await CreateHandler().Handle
        (
            new DeleteChatCommand(thread.Id.Value),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("Chat.NotFound", error.Code);
        Assert.Single(_chats.Threads);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundForTemporaryChat()
    {
        ChatThread thread = CreateThread(isTemporary: true);
        _chats.Seed(thread);

        ErrorOr<Deleted> result = await CreateHandler().Handle
        (
            new DeleteChatCommand(thread.Id.Value),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Single(_chats.Threads);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsValidationErrorsWithoutDeleting()
    {
        ChatThread thread = CreateThread();
        _chats.Seed(thread);

        ErrorOr<Deleted> result = await CreateHandler(userId: string.Empty).Handle
        (
            new DeleteChatCommand(Guid.Empty),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, error => error.Type == ErrorType.Validation);
        Assert.Contains(result.Errors, error => error.Code == "ChatId.Empty");
        Assert.Single(_chats.Threads);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    private DeleteChatHandler CreateHandler(string userId = "auth0|user-1") => new
    (
        userContext: new FakeUserContext(userId),
        chats: _chats,
        unitOfWork: _unitOfWork
    );

    private static ChatThread CreateThread
    (
        string userId = "auth0|user-1",
        bool isTemporary = false
    ) => ChatThread.Create
    (
        userId: UserId.Create(userId).Value,
        title: ChatTitle.Create("Planning chat").Value,
        firstUserMessage: MessageContent.Create("Hello").Value,
        createdAt: CreatedAt,
        isTemporary: isTemporary
    );
}