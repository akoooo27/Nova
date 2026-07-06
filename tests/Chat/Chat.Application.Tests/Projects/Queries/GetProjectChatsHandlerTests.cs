using Chat.Application.Chats.Queries.GetChats;
using Chat.Application.Projects.Errors;
using Chat.Application.Projects.Queries.GetProjectChats;
using Chat.Application.Tests.FavoriteModels;
using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Projects.Queries;

public sealed class GetProjectChatsHandlerTests
{
    [Fact]
    public async Task HandleReadsProjectChatsForCurrentUserWithPaging()
    {
        UserId userId = UserId.FromDatabase("auth0|user-1");
        Guid projectId = Guid.CreateVersion7();
        ProjectChatListReadModel readModel = new
        (
            Chats:
            [
                new ChatSummaryReadModel
                (
                    Id: Guid.CreateVersion7(),
                    Title: "Kickoff notes",
                    IsPinned: false,
                    PinnedAt: null,
                    IsArchived: false,
                    IsTemporary: false,
                    CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
                    UpdatedAt: DateTimeOffset.UtcNow
                )
            ],
            Total: 1,
            Limit: 20,
            Offset: 0
        );
        FakeProjectChatListReader reader = new(readModel);
        GetProjectChatsHandler handler = new
        (
            userContext: new FakeUserContext(userId.Value),
            reader: reader
        );

        ErrorOr<ProjectChatListReadModel> result = await handler.Handle
        (
            new GetProjectChatsQuery(ProjectId: projectId, Limit: 20, Offset: 0),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Same(readModel, result.Value);
        Assert.Equal(projectId, reader.RequestedProjectId?.Value);
        Assert.Equal(userId, reader.RequestedUserId);
        Assert.Equal(20, reader.RequestedLimit);
        Assert.Equal(0, reader.RequestedOffset);
        Assert.Equal(1, reader.GetCallCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWhenProjectMissing()
    {
        Guid projectId = Guid.CreateVersion7();
        FakeProjectChatListReader reader = new(readModel: null);
        GetProjectChatsHandler handler = new
        (
            userContext: new FakeUserContext("auth0|user-1"),
            reader: reader
        );

        ErrorOr<ProjectChatListReadModel> result = await handler.Handle
        (
            new GetProjectChatsQuery(ProjectId: projectId, Limit: 20, Offset: 0),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal(ProjectOperationErrors.ProjectNotFound(ProjectId.FromDatabase(projectId)).Code, result.FirstError.Code);
        Assert.Equal(1, reader.GetCallCount);
    }

    [Fact]
    public async Task HandleReturnsErrorAndSkipsReaderWhenUserIdMissing()
    {
        FakeProjectChatListReader reader = new(readModel: null);
        GetProjectChatsHandler handler = new
        (
            userContext: new FakeUserContext(string.Empty),
            reader: reader
        );

        ErrorOr<ProjectChatListReadModel> result = await handler.Handle
        (
            new GetProjectChatsQuery(ProjectId: Guid.CreateVersion7(), Limit: 20, Offset: 0),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal(0, reader.GetCallCount);
    }

    [Fact]
    public async Task HandleReturnsErrorAndSkipsReaderWhenProjectIdEmpty()
    {
        FakeProjectChatListReader reader = new(readModel: null);
        GetProjectChatsHandler handler = new
        (
            userContext: new FakeUserContext("auth0|user-1"),
            reader: reader
        );

        ErrorOr<ProjectChatListReadModel> result = await handler.Handle
        (
            new GetProjectChatsQuery(ProjectId: Guid.Empty, Limit: 20, Offset: 0),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal(0, reader.GetCallCount);
    }
}