using Chat.Application.Chats.Commands.ArchiveAllChats;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Tests.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Chats.Commands;

public sealed class ArchiveAllChatsHandlerTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeChatRepository _chats = new();
    private readonly TurnFakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task HandleArchivesOnlyOwnedActiveNonTemporaryChats()
    {
        ChatThread active = CreateThread();
        ChatThread inProject = CreateThread();
        inProject.MoveToProject(ProjectId.New(), CreatedAt.AddMinutes(1));
        ChatThread alreadyArchived = CreateThread();
        alreadyArchived.Archive();
        ChatThread temporary = CreateThread(isTemporary: true);
        ChatThread foreign = CreateThread(userId: "auth0|other-user");

        _chats.Seed(active);
        _chats.Seed(inProject);
        _chats.Seed(alreadyArchived);
        _chats.Seed(temporary);
        _chats.Seed(foreign);

        ErrorOr<Success> result = await CreateHandler().Handle
        (
            new ArchiveAllChatsCommand(),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.True(active.IsArchived);
        Assert.Equal(CreatedAt, active.UpdatedAt);
        Assert.True(inProject.IsArchived);
        Assert.True(alreadyArchived.IsArchived);
        Assert.False(temporary.IsArchived);
        Assert.False(foreign.IsArchived);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleSucceedsWhenUserHasNoChats()
    {
        ErrorOr<Success> result = await CreateHandler().Handle
        (
            new ArchiveAllChatsCommand(),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsValidationErrorForEmptyUserContext()
    {
        ChatThread thread = CreateThread();
        _chats.Seed(thread);

        ErrorOr<Success> result = await CreateHandler(userId: string.Empty).Handle
        (
            new ArchiveAllChatsCommand(),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.False(thread.IsArchived);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    private ArchiveAllChatsHandler CreateHandler(string userId = "auth0|user-1") => new
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
