using Chat.Domain.Chats;
using Chat.Domain.Projects.ValueObjects;

using ErrorOr;

namespace Chat.Domain.Tests.Chats;

public sealed class ChatThreadProjectTests
{
    private static ChatThread CreateThread(bool isTemporary = false) =>
        ChatThread.Create
        (
            userId: TestChatFactory.CreateUserId(),
            title: TestChatFactory.CreateTitle(),
            firstUserMessage: TestChatFactory.CreateContent("Hello"),
            createdAt: TestChatFactory.CreatedAt,
            isTemporary: isTemporary
        );

    [Fact]
    public void CreateDefaultsProjectIdToNull()
    {
        ChatThread chat = CreateThread();

        Assert.Null(chat.ProjectId);
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
        ChatThread chat = CreateThread();
        chat.MoveToProject(ProjectId.New(), TestChatFactory.CreatedAt.AddMinutes(2));
        DateTimeOffset later = TestChatFactory.CreatedAt.AddMinutes(5);

        chat.RemoveFromProject(later);

        Assert.Null(chat.ProjectId);
        Assert.Equal(later, chat.UpdatedAt);
    }
}