using Chat.Application.Projects.Commands.Create;
using Chat.Application.Projects.Results;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Tests.Turns;

using ErrorOr;

namespace Chat.Application.Tests.Projects;

public sealed class CreateProjectHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeProjectRepository _projects = new();
    private readonly TurnFakeUnitOfWork _unitOfWork = new();

    private CreateProjectHandler CreateHandler() => new
    (
        userContext: new FakeUserContext("auth0|user-1"),
        projects: _projects,
        unitOfWork: _unitOfWork,
        dateTimeProvider: new FakeDateTimeProvider(Now)
    );

    [Fact]
    public async Task HandlePersistsProjectAndReturnsResult()
    {
        CreateProjectHandler handler = CreateHandler();

        ErrorOr<ProjectResult> result = await handler.Handle
        (
            new CreateProjectCommand(Name: "  Dollars  ", Instructions: "Only finance.", Emoji: "currency-dollar", Theme: "#f6c543"),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Equal("Dollars", result.Value.Name);
        Assert.Equal("Only finance.", result.Value.Instructions);
        Assert.Equal("currency-dollar", result.Value.Emoji);
        Assert.Equal("#F6C543", result.Value.Theme);
        Assert.Equal(Now, result.Value.CreatedAt);
        Assert.Equal(Now, result.Value.UpdatedAt);
        Assert.Single(_projects.Projects);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleTreatsBlankOptionalFieldsAsCleared()
    {
        CreateProjectHandler handler = CreateHandler();

        ErrorOr<ProjectResult> result = await handler.Handle
        (
            new CreateProjectCommand(Name: "Dollars", Instructions: "   ", Emoji: null, Theme: "  "),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Null(result.Value.Instructions);
        Assert.Null(result.Value.Emoji);
        Assert.Null(result.Value.Theme);
    }

    [Fact]
    public async Task HandleReturnsAccumulatedValidationErrorsWithoutSaving()
    {
        CreateProjectHandler handler = CreateHandler();

        ErrorOr<ProjectResult> result = await handler.Handle
        (
            new CreateProjectCommand(Name: "", Instructions: null, Emoji: null, Theme: "not-a-color"),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == "ProjectName.Required");
        Assert.Contains(result.Errors, e => e.Code == "ProjectTheme.Invalid");
        Assert.Empty(_projects.Projects);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }
}