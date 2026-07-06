using Chat.Application.Projects.Commands.Delete;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Tests.Turns;
using Chat.Domain.Projects;
using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Projects;

public sealed class DeleteProjectHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeProjectRepository _projects = new();
    private readonly TurnFakeUnitOfWork _unitOfWork = new();

    private DeleteProjectHandler CreateHandler() => new
    (
        userContext: new FakeUserContext("auth0|user-1"),
        projects: _projects,
        unitOfWork: _unitOfWork
    );

    private Project SeedProject(string userId = "auth0|user-1")
    {
        Project project = Project.Create
        (
            userId: UserId.Create(userId).Value,
            name: ProjectName.Create("Dollars").Value,
            instructions: null,
            emoji: null,
            theme: null,
            createdAt: Now
        );

        _projects.AddExisting(project);

        return project;
    }

    [Fact]
    public async Task HandleRemovesProject()
    {
        Project project = SeedProject();
        DeleteProjectHandler handler = CreateHandler();

        ErrorOr<Success> result = await handler.Handle(new DeleteProjectCommand(project.Id.Value), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Empty(_projects.Projects);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWhenProjectBelongsToAnotherUser()
    {
        Project project = SeedProject(userId: "auth0|other-user");
        DeleteProjectHandler handler = CreateHandler();

        ErrorOr<Success> result = await handler.Handle(new DeleteProjectCommand(project.Id.Value), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Project.NotFound", result.FirstError.Code);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }
}