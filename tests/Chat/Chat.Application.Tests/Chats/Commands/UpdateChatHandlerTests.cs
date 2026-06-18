using Chat.Application.Chats.Commands.UpdateChat;
using Chat.Application.Chats.Results;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Tests.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Chats.Commands;

public sealed class UpdateChatHandlerTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = CreatedAt.AddMinutes(30);

    private readonly FakeChatRepository _chats = new();
    private readonly TurnFakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task HandleUpdatesEditableMetadataAndReturnsFullThreadState()
    {
        ChatThread thread = CreateThread();
        _chats.Seed(thread);
        UpdateChatHandler handler = CreateHandler();
        UpdateChatCommand command = new
        (
            ChatId: thread.Id.Value,
            Title: "  Renamed chat  ",
            IsPinned: true,
            IsArchived: true
        );

        ErrorOr<ChatThreadResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Renamed chat", thread.Title.Value);
        Assert.True(thread.IsPinned);
        Assert.Equal(Now, thread.PinnedAt);
        Assert.True(thread.IsArchived);
        Assert.Equal(CreatedAt, thread.UpdatedAt);
        Assert.Equal(1, _unitOfWork.SaveCount);

        ChatThreadResult response = result.Value;
        Assert.Equal(thread.Id.Value, response.Id);
        Assert.Equal("Renamed chat", response.Title);
        Assert.True(response.IsPinned);
        Assert.Equal(Now, response.PinnedAt);
        Assert.True(response.IsArchived);
        Assert.False(response.IsTemporary);
        Assert.Equal(CreatedAt, response.CreatedAt);
        Assert.Equal(CreatedAt, response.UpdatedAt);
    }

    [Fact]
    public async Task HandleUnpinsAndUnarchivesWhenFullStateSetsFlagsFalse()
    {
        ChatThread thread = CreateThread();
        thread.Pin(CreatedAt.AddMinutes(5));
        thread.Archive();
        _chats.Seed(thread);
        UpdateChatHandler handler = CreateHandler();

        ErrorOr<ChatThreadResult> result = await handler.Handle
        (
            new UpdateChatCommand
            (
                ChatId: thread.Id.Value,
                Title: "Renamed chat",
                IsPinned: false,
                IsArchived: false
            ),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Equal("Renamed chat", thread.Title.Value);
        Assert.False(thread.IsPinned);
        Assert.Null(thread.PinnedAt);
        Assert.False(thread.IsArchived);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleKeepsOriginalPinnedTimestampWhenThreadStaysPinned()
    {
        ChatThread thread = CreateThread();
        DateTimeOffset originalPinnedAt = CreatedAt.AddMinutes(5);
        thread.Pin(originalPinnedAt);
        _chats.Seed(thread);
        UpdateChatHandler handler = CreateHandler();

        ErrorOr<ChatThreadResult> result = await handler.Handle
        (
            new UpdateChatCommand
            (
                ChatId: thread.Id.Value,
                Title: thread.Title.Value,
                IsPinned: true,
                IsArchived: false
            ),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Equal(originalPinnedAt, thread.PinnedAt);
        Assert.Equal(originalPinnedAt, result.Value.PinnedAt);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWithoutSavingWhenChatDoesNotBelongToUser()
    {
        ChatThread thread = CreateThread(userId: "auth0|other-user");
        _chats.Seed(thread);
        UpdateChatHandler handler = CreateHandler();

        ErrorOr<ChatThreadResult> result = await handler.Handle
        (
            new UpdateChatCommand
            (
                ChatId: thread.Id.Value,
                Title: "Renamed chat",
                IsPinned: true,
                IsArchived: true
            ),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("Chat.NotFound", error.Code);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsValidationErrorsWithoutSaving()
    {
        UpdateChatHandler handler = CreateHandler();

        ErrorOr<ChatThreadResult> result = await handler.Handle
        (
            new UpdateChatCommand
            (
                ChatId: Guid.Empty,
                Title: "",
                IsPinned: false,
                IsArchived: false
            ),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, error => error.Code == "ChatId.Empty");
        Assert.Contains(result.Errors, error => error.Code == "ChatTitle.Required");
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    private UpdateChatHandler CreateHandler() => new
    (
        userContext: new FakeUserContext("auth0|user-1"),
        chats: _chats,
        unitOfWork: _unitOfWork,
        dateTimeProvider: new FakeDateTimeProvider(Now)
    );

    private static ChatThread CreateThread(string userId = "auth0|user-1") => ChatThread.Create
    (
        userId: UserId.Create(userId).Value,
        title: ChatTitle.Create("Planning chat").Value,
        firstUserMessage: MessageContent.Create("Hello").Value,
        createdAt: CreatedAt
    );
}
