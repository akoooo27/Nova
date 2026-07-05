using Chat.Application.Chats.Commands.SetChatProject;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Tests.Projects;
using Chat.Application.Tests.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Projects;
using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Chats.Commands;

public sealed class SetChatProjectHandlerTests
{
    private const string OwnerId = "auth0|user-1";

    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = CreatedAt.AddMinutes(30);

    private readonly FakeChatRepository _chats = new();
    private readonly FakeProjectRepository _projects = new();
    private readonly TurnFakeUnitOfWork _unitOfWork = new();

    private SetChatProjectHandler CreateHandler() => new
    (
        userContext: new FakeUserContext(OwnerId),
        chats: _chats,
        projects: _projects,
        unitOfWork: _unitOfWork,
        dateTimeProvider: new FakeDateTimeProvider(Now)
    );

    private ChatThread SeedChat(string userId = OwnerId, bool isTemporary = false)
    {
        ChatThread thread = ChatThread.Create
        (
            userId: UserId.Create(userId).Value,
            title: ChatTitle.Create("Planning chat").Value,
            firstUserMessage: MessageContent.Create("Hello").Value,
            createdAt: CreatedAt,
            isTemporary: isTemporary
        );

        _chats.Seed(thread);

        return thread;
    }

    private Project SeedProject(string userId = OwnerId)
    {
        Project project = Project.Create
        (
            userId: UserId.Create(userId).Value,
            name: ProjectName.Create("Dollars").Value,
            instructions: null,
            emoji: null,
            theme: null,
            createdAt: CreatedAt
        );

        _projects.AddExisting(project);

        return project;
    }

    [Fact]
    public async Task HandleMovesChatIntoProjectWhenProjectIdProvided()
    {
        ChatThread chat = SeedChat();
        Project project = SeedProject();
        SetChatProjectHandler handler = CreateHandler();

        ErrorOr<Success> result = await handler.Handle
        (
            new SetChatProjectCommand(chat.Id.Value, project.Id.Value),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Equal(project.Id, chat.ProjectId);
        Assert.Equal(Now, chat.UpdatedAt);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleMovesChatOutOfProjectWhenProjectIdNull()
    {
        ChatThread chat = SeedChat();
        Project project = SeedProject();
        chat.MoveToProject(project.Id, CreatedAt.AddMinutes(5));
        SetChatProjectHandler handler = CreateHandler();

        ErrorOr<Success> result = await handler.Handle
        (
            new SetChatProjectCommand(chat.Id.Value, ProjectId: null),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Null(chat.ProjectId);
        Assert.Equal(Now, chat.UpdatedAt);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWhenChatBelongsToAnotherUser()
    {
        ChatThread chat = SeedChat(userId: "auth0|other-user");
        Project project = SeedProject();
        SetChatProjectHandler handler = CreateHandler();

        ErrorOr<Success> result = await handler.Handle
        (
            new SetChatProjectCommand(chat.Id.Value, project.Id.Value),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal("Chat.NotFound", result.FirstError.Code);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWhenTargetProjectBelongsToAnotherUser()
    {
        ChatThread chat = SeedChat();
        Project project = SeedProject(userId: "auth0|other-user");
        SetChatProjectHandler handler = CreateHandler();

        ErrorOr<Success> result = await handler.Handle
        (
            new SetChatProjectCommand(chat.Id.Value, project.Id.Value),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal("Project.NotFound", result.FirstError.Code);
        Assert.Null(chat.ProjectId);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsConflictWhenMovingTemporaryChatIntoProject()
    {
        ChatThread chat = SeedChat(isTemporary: true);
        Project project = SeedProject();
        SetChatProjectHandler handler = CreateHandler();

        ErrorOr<Success> result = await handler.Handle
        (
            new SetChatProjectCommand(chat.Id.Value, project.Id.Value),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal("Chat.CannotAddTemporaryChatToProject", result.FirstError.Code);
        Assert.Null(chat.ProjectId);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }
}