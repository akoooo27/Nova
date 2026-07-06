using Chat.Domain.Projects;
using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Domain.Tests.Projects;

public sealed class ProjectTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);

    private static Project CreateProject
    (
        ProjectInstructions? instructions = null,
        ProjectEmoji? emoji = null,
        ProjectTheme? theme = null
    ) => Project.Create
    (
        userId: UserId.Create("auth0|user-1").Value,
        name: ProjectName.Create("Dollars").Value,
        instructions: instructions,
        emoji: emoji,
        theme: theme,
        createdAt: CreatedAt
    );

    [Fact]
    public void CreateAssignsAllStateAndSetsTimestampsToCreatedAt()
    {
        Project project = CreateProject
        (
            instructions: ProjectInstructions.Create("Only finance.").Value,
            emoji: ProjectEmoji.Create("currency-dollar").Value,
            theme: ProjectTheme.Create("#F6C543").Value
        );

        Assert.NotEqual(Guid.Empty, project.Id.Value);
        Assert.Equal(UserId.Create("auth0|user-1").Value, project.UserId);
        Assert.Equal("Dollars", project.Name.Value);
        Assert.Equal("Only finance.", project.Instructions!.Value);
        Assert.Equal("currency-dollar", project.Emoji!.Value);
        Assert.Equal("#F6C543", project.Theme!.Value);
        Assert.Equal(CreatedAt, project.CreatedAt);
        Assert.Equal(CreatedAt, project.UpdatedAt);
    }

    [Fact]
    public void CreateLeavesOptionalStateNullWhenNotProvided()
    {
        Project project = CreateProject();

        Assert.Null(project.Instructions);
        Assert.Null(project.Emoji);
        Assert.Null(project.Theme);
    }

    [Fact]
    public void RenameUpdatesNameAndBumpsUpdatedAt()
    {
        Project project = CreateProject();
        DateTimeOffset later = CreatedAt.AddMinutes(5);

        project.Rename(ProjectName.Create("Euros").Value, later);

        Assert.Equal("Euros", project.Name.Value);
        Assert.Equal(later, project.UpdatedAt);
        Assert.Equal(CreatedAt, project.CreatedAt);
    }

    [Fact]
    public void UpdateInstructionsAssignsValueAndBumpsUpdatedAt()
    {
        Project project = CreateProject();
        DateTimeOffset later = CreatedAt.AddMinutes(5);

        project.UpdateInstructions(ProjectInstructions.Create("Be terse.").Value, later);

        Assert.Equal("Be terse.", project.Instructions!.Value);
        Assert.Equal(later, project.UpdatedAt);
    }

    [Fact]
    public void UpdateInstructionsWithNullClearsAndBumpsUpdatedAt()
    {
        Project project = CreateProject(instructions: ProjectInstructions.Create("Be terse.").Value);
        DateTimeOffset later = CreatedAt.AddMinutes(5);

        project.UpdateInstructions(null, later);

        Assert.Null(project.Instructions);
        Assert.Equal(later, project.UpdatedAt);
    }

    [Fact]
    public void UpdateAppearanceAssignsEmojiAndThemeAndBumpsUpdatedAt()
    {
        Project project = CreateProject();
        DateTimeOffset later = CreatedAt.AddMinutes(5);

        project.UpdateAppearance
        (
            emoji: ProjectEmoji.Create("rocket").Value,
            theme: ProjectTheme.Create("#00FF00").Value,
            updatedAt: later
        );

        Assert.Equal("rocket", project.Emoji!.Value);
        Assert.Equal("#00FF00", project.Theme!.Value);
        Assert.Equal(later, project.UpdatedAt);
    }

    [Fact]
    public void UpdateAppearanceWithNullClearsEmojiAndThemeAndBumpsUpdatedAt()
    {
        Project project = CreateProject
        (
            emoji: ProjectEmoji.Create("rocket").Value,
            theme: ProjectTheme.Create("#00FF00").Value
        );
        DateTimeOffset later = CreatedAt.AddMinutes(5);

        project.UpdateAppearance(emoji: null, theme: null, updatedAt: later);

        Assert.Null(project.Emoji);
        Assert.Null(project.Theme);
        Assert.Equal(later, project.UpdatedAt);
    }
}