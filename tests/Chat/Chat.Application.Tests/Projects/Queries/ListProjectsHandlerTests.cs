using Chat.Application.Projects.Queries.ListProjects;
using Chat.Application.Tests.FavoriteModels;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Projects.Queries;

public sealed class ListProjectsHandlerTests
{
    [Fact]
    public async Task HandleReadsProjectsForCurrentUserWithPaging()
    {
        UserId userId = UserId.FromDatabase("auth0|user-1");
        ProjectListReadModel readModel = new
        (
            Projects:
            [
                new ProjectSummaryReadModel
                (
                    Id: Guid.CreateVersion7(),
                    Name: "Launch plan",
                    Instructions: "Stay concise.",
                    Emoji: "🚀",
                    Theme: "indigo",
                    CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
                    UpdatedAt: DateTimeOffset.UtcNow
                )
            ],
            Total: 1,
            Limit: 20,
            Offset: 0
        );
        FakeProjectListReader reader = new(readModel);
        ListProjectsHandler handler = new
        (
            userContext: new FakeUserContext(userId.Value),
            reader: reader
        );

        ErrorOr<ProjectListReadModel> result = await handler.Handle
        (
            new ListProjectsQuery(Limit: 20, Offset: 0),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Same(readModel, result.Value);
        Assert.Equal(userId, reader.RequestedUserId);
        Assert.Equal(20, reader.RequestedLimit);
        Assert.Equal(0, reader.RequestedOffset);
        Assert.Equal(1, reader.GetCallCount);
    }

    [Fact]
    public async Task HandleReturnsErrorAndSkipsReaderWhenUserIdMissing()
    {
        ProjectListReadModel readModel = new([], Total: 0, Limit: 20, Offset: 0);
        FakeProjectListReader reader = new(readModel);
        ListProjectsHandler handler = new
        (
            userContext: new FakeUserContext(string.Empty),
            reader: reader
        );

        ErrorOr<ProjectListReadModel> result = await handler.Handle
        (
            new ListProjectsQuery(Limit: 20, Offset: 0),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal(0, reader.GetCallCount);
    }
}