using Chat.Application.Turns;
using Chat.Domain.Personalizations;
using Chat.Domain.Personalizations.ValueObjects;
using Chat.Domain.Projects;
using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Application.Tests.Turns;

public sealed class PersonalizationSystemPromptTests
{
    private const string Base = "You are Nova, a helpful AI assistant.";

    private static readonly DateTimeOffset When = new(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);

    private static Personalization NewPersonalization() =>
        Personalization.Create(UserId.Create("auth0|user-1").Value);

    private static Project NewProject(string instructions) => Project.Create
    (
        userId: UserId.Create("auth0|user-1").Value,
        name: ProjectName.Create("Dollars").Value,
        instructions: ProjectInstructions.Create(instructions).Value,
        emoji: null,
        theme: null,
        createdAt: When
    );

    [Fact]
    public void ComposeWhenPersonalizationIsNullReturnsBasePromptUnchanged()
    {
        string result = PersonalizationSystemPrompt.Compose(Base, null, null);

        Assert.Equal(Base, result);
    }

    [Fact]
    public void ComposeWhenAggregateHasNoInstructionsOrProfileReturnsBasePromptUnchanged()
    {
        string result = PersonalizationSystemPrompt.Compose(Base, null, NewPersonalization());

        Assert.Equal(Base, result);
    }

    [Fact]
    public void ComposeWithCustomInstructionsOnlyAppendsInstructionsSectionAndFraming()
    {
        Personalization personalization = NewPersonalization();
        personalization.UpdateInstructions(CustomInstructions.Create("Always answer in British English.").Value);

        string result = PersonalizationSystemPrompt.Compose(Base, null, personalization);

        Assert.StartsWith(Base, result, StringComparison.Ordinal);
        Assert.Contains("does NOT override your core identity", result, StringComparison.Ordinal);
        Assert.Contains
        (
            "<custom_instructions>\nAlways answer in British English.\n</custom_instructions>",
            result,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("<user_profile>", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeWithProfileOnlyRendersOnlyPresentFields()
    {
        Personalization personalization = NewPersonalization();
        personalization.UpdateUserProfile(UserProfile.Create
        (
            name: UserName.Create("Aki").Value,
            role: null,
            about: AboutUser.Create("Loves Redis").Value
        ));

        string result = PersonalizationSystemPrompt.Compose(Base, null, personalization);

        Assert.Contains("<user_profile>", result, StringComparison.Ordinal);
        Assert.Contains("Name: Aki", result, StringComparison.Ordinal);
        Assert.Contains("About: Loves Redis", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Role:", result, StringComparison.Ordinal);
        Assert.DoesNotContain("<custom_instructions>", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeWithBothEmitsProfileBeforeInstructions()
    {
        Personalization personalization = NewPersonalization();
        personalization.UpdateUserProfile(UserProfile.Create
        (
            name: UserName.Create("Aki").Value,
            role: UserRole.Create("Engineer").Value,
            about: null
        ));
        personalization.UpdateInstructions(CustomInstructions.Create("Be concise.").Value);

        string result = PersonalizationSystemPrompt.Compose(Base, null, personalization);

        int profileIndex = result.IndexOf("<user_profile>", StringComparison.Ordinal);
        int instructionsIndex = result.IndexOf("<custom_instructions>", StringComparison.Ordinal);

        Assert.True(profileIndex >= 0);
        Assert.True(instructionsIndex > profileIndex);
        Assert.Contains("Role: Engineer", result, StringComparison.Ordinal);
        Assert.Contains("Be concise.", result, StringComparison.Ordinal);
        Assert.DoesNotContain("About:", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeWithProjectOnlyEmitsProjectSectionAndNoPersonalization()
    {
        Project project = NewProject("Only discuss finance.");

        string result = PersonalizationSystemPrompt.Compose(Base, project, null);

        Assert.Contains("<project_instructions>\nOnly discuss finance.\n</project_instructions>", result, StringComparison.Ordinal);
        Assert.DoesNotContain("<custom_instructions>", result, StringComparison.Ordinal);
        Assert.DoesNotContain("<user_profile>", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeWithProjectAndPersonalizationEmitsProjectSectionFirst()
    {
        Project project = NewProject("Only discuss finance.");

        Personalization personalization = NewPersonalization();
        personalization.UpdateInstructions(CustomInstructions.Create("Be concise.").Value);

        string result = PersonalizationSystemPrompt.Compose(Base, project, personalization);

        int projectIndex = result.IndexOf("<project_instructions>", StringComparison.Ordinal);
        int customIndex = result.IndexOf("<custom_instructions>", StringComparison.Ordinal);

        Assert.True(projectIndex >= 0);
        Assert.True(customIndex > projectIndex);
        Assert.Contains("Only discuss finance.", result, StringComparison.Ordinal);
        Assert.Contains("Be concise.", result, StringComparison.Ordinal);
    }
}