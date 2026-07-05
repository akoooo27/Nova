using Chat.Domain.Chats;
using Chat.Domain.Projects.ValueObjects;

using ErrorOr;

namespace Chat.Domain.Tests.Chats;

public sealed class ChatThreadProjectTests
{
    private static ChatThread CreateThread(bool isTemporary = false, ProjectId? projectId = null) =>
        ChatThread.Create
        (
            userId: TestChatFactory.CreateUserId(),
            title: TestChatFactory.CreateTitle(),
            firstUserMessage: TestChatFactory.CreateContent("Hello"),
            createdAt: TestChatFactory.CreatedAt,
            isTemporary: isTemporary,
            projectId: projectId
        );

    [Fact]
    public void CreateDefaultsProjectIdToNull()
    {
        ChatThread chat = CreateThread();

        Assert.Null(chat.ProjectId);
    }

    [Fact]
    public void CreateAssignsProjectIdWhenProvided()
    {
        ProjectId projectId = ProjectId.New();

        ChatThread chat = CreateThread(projectId: projectId);

        Assert.Equal(projectId, chat.ProjectId);
    }

    [Fact]
    public void MoveToProjectAssignsProjectIdAndBumpsUpdatedAt()
    {
        ChatThread chat = CreateThread();
        ProjectId projectId = ProjectId.New();
        DateTimeOffset later = TestChatFactory.CreatedAt.AddMinutes(5);

        ErrorOr<Success> result = chat.MoveToProject(projectId, later);

        Assert.False(result.IsError);
        Assert.Equal(projectId, chat.ProjectId);
        Assert.Equal(later, chat.UpdatedAt);
    }

    [Fact]
    public void MoveToProjectReturnsConflictWhenChatIsTemporary()
    {
        ChatThread chat = CreateThread(isTemporary: true);
        DateTimeOffset later = TestChatFactory.CreatedAt.AddMinutes(5);

        ErrorOr<Success> result = chat.MoveToProject(ProjectId.New(), later);

        Assert.True(result.IsError);
        Assert.Equal("Chat.CannotAddTemporaryChatToProject", result.FirstError.Code);
        Assert.Null(chat.ProjectId);
    }

    [Fact]
    public void RemoveFromProjectClearsProjectIdAndBumpsUpdatedAt()
    {
        ProjectId projectId = ProjectId.New();
        ChatThread chat = CreateThread(projectId: projectId);
        DateTimeOffset later = TestChatFactory.CreatedAt.AddMinutes(5);

        chat.RemoveFromProject(later);

        Assert.Null(chat.ProjectId);
        Assert.Equal(later, chat.UpdatedAt);
    }
}