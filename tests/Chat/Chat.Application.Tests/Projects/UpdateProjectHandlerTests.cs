using Chat.Application.Projects.Commands.Update;
using Chat.Application.Projects.Results;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Tests.Turns;
using Chat.Domain.Projects;
using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Projects;

public sealed class UpdateProjectHandlerTests
{
    private static readonly DateTimeOffset SeededAt = new(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedNow = SeededAt.AddHours(1);

    private readonly FakeProjectRepository _projects = new();
    private readonly TurnFakeUnitOfWork _unitOfWork = new();

    private UpdateProjectHandler CreateHandler() => new
    (
        userContext: new FakeUserContext("auth0|user-1"),
        projects: _projects,
        unitOfWork: _unitOfWork,
        dateTimeProvider: new FakeDateTimeProvider(UpdatedNow)
    );

    private Project SeedProject
    (
        string userId = "auth0|user-1",
        ProjectInstructions? instructions = null,
        ProjectEmoji? emoji = null,
        ProjectTheme? theme = null
    )
    {
        Project project = Project.Create
        (
            userId: UserId.Create(userId).Value,
            name: ProjectName.Create("Dollars").Value,
            instructions: instructions,
            emoji: emoji,
            theme: theme,
            createdAt: SeededAt
        );

        _projects.AddExisting(project);

        return project;
    }

    [Fact]
    public async Task HandleUpdatesEditableStateAndBumpsUpdatedAt()
    {
        Project project = SeedProject();
        UpdateProjectHandler handler = CreateHandler();

        ErrorOr<ProjectResult> result = await handler.Handle
        (
            new UpdateProjectCommand(project.Id.Value, Name: "  Euros  ", Instructions: "Only euros.", Emoji: "flag", Theme: "#00ff00"),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Equal("Euros", result.Value.Name);
        Assert.Equal("Only euros.", result.Value.Instructions);
        Assert.Equal("flag", result.Value.Emoji);
        Assert.Equal("#00FF00", result.Value.Theme);
        Assert.Equal(SeededAt, result.Value.CreatedAt);
        Assert.Equal(UpdatedNow, result.Value.UpdatedAt);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleClearsOptionalFieldsWhenBlank()
    {
        Project project = SeedProject
        (
            instructions: ProjectInstructions.Create("Only finance.").Value,
            emoji: ProjectEmoji.Create("currency-dollar").Value,
            theme: ProjectTheme.Create("#F6C543").Value
        );
        UpdateProjectHandler handler = CreateHandler();

        ErrorOr<ProjectResult> result = await handler.Handle
        (
            new UpdateProjectCommand(project.Id.Value, Name: "Dollars", Instructions: "   ", Emoji: null, Theme: "  "),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Null(result.Value.Instructions);
        Assert.Null(result.Value.Emoji);
        Assert.Null(result.Value.Theme);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWhenProjectBelongsToAnotherUser()
    {
        Project project = SeedProject(userId: "auth0|other-user");
        UpdateProjectHandler handler = CreateHandler();

        ErrorOr<ProjectResult> result = await handler.Handle
        (
            new UpdateProjectCommand(project.Id.Value, Name: "Euros", Instructions: null, Emoji: null, Theme: null),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal("Project.NotFound", result.FirstError.Code);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsValidationErrorsWithoutLoadingOrSaving()
    {
        SeedProject();
        UpdateProjectHandler handler = CreateHandler();

        ErrorOr<ProjectResult> result = await handler.Handle
        (
            new UpdateProjectCommand(Guid.NewGuid(), Name: "", Instructions: null, Emoji: null, Theme: "not-a-color"),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == "ProjectName.Required");
        Assert.Contains(result.Errors, e => e.Code == "ProjectTheme.Invalid");
        Assert.Equal(0, _unitOfWork.SaveCount);
    }
}