using Chat.Domain;
using Chat.Domain.Projects.ValueObjects;

using ErrorOr;

namespace Chat.Domain.Tests.Projects;

public sealed class ProjectValueObjectTests
{
    [Fact]
    public void ProjectIdNewReturnsNonEmptyId()
    {
        ProjectId id = ProjectId.New();

        Assert.NotEqual(Guid.Empty, id.Value);
        Assert.Equal(id.Value.ToString(), id.ToString());
    }

    [Fact]
    public void ProjectIdCreateReturnsValueWhenGuidIsNotEmpty()
    {
        Guid value = Guid.NewGuid();

        ErrorOr<ProjectId> result = ProjectId.Create(value);

        Assert.False(result.IsError);
        Assert.Equal(value, result.Value.Value);
        Assert.Equal(value.ToString(), result.Value.ToString());
    }

    [Fact]
    public void ProjectIdCreateReturnsEmptyValidationWhenGuidIsEmpty()
    {
        ErrorOr<ProjectId> result = ProjectId.Create(Guid.Empty);

        AssertError(result, ErrorType.Validation, "ProjectId.Empty");
    }

    [Fact]
    public void ProjectIdFromDatabaseThrowsDomainExceptionWhenGuidIsEmpty()
    {
        Assert.Throws<DomainException>(() => ProjectId.FromDatabase(Guid.Empty));
    }

    [Fact]
    public void ProjectNameCreateTrimsWhitespaceWhenValueIsValid()
    {
        ErrorOr<ProjectName> result = ProjectName.Create("  Dollars  ");

        Assert.False(result.IsError);
        Assert.Equal("Dollars", result.Value.Value);
        Assert.Equal("Dollars", result.Value.ToString());
    }

    [Fact]
    public void ProjectNameCreateReturnsRequiredValidationWhenValueIsBlank()
    {
        ErrorOr<ProjectName> result = ProjectName.Create("   ");

        AssertError(result, ErrorType.Validation, "ProjectName.Required");
    }

    [Fact]
    public void ProjectNameCreateReturnsTooLongValidationWhenValueExceedsMaxLength()
    {
        ErrorOr<ProjectName> result = ProjectName.Create(new string('a', ProjectName.MaxLength + 1));

        AssertError(result, ErrorType.Validation, "ProjectName.TooLong");
    }

    [Fact]
    public void ProjectNameFromDatabaseThrowsDomainExceptionWhenValueIsBlank()
    {
        Assert.Throws<DomainException>(() => ProjectName.FromDatabase(""));
    }

    [Fact]
    public void ProjectNameFromDatabaseThrowsDomainExceptionWhenValueExceedsMaxLength()
    {
        Assert.Throws<DomainException>(() => ProjectName.FromDatabase(new string('a', ProjectName.MaxLength + 1)));
    }

    [Fact]
    public void ProjectEmojiCreateTrimsWhitespaceWhenValueIsValid()
    {
        ErrorOr<ProjectEmoji> result = ProjectEmoji.Create("  currency-dollar  ");

        Assert.False(result.IsError);
        Assert.Equal("currency-dollar", result.Value.Value);
        Assert.Equal("currency-dollar", result.Value.ToString());
    }

    [Fact]
    public void ProjectEmojiCreateReturnsRequiredValidationWhenValueIsBlank()
    {
        ErrorOr<ProjectEmoji> result = ProjectEmoji.Create("   ");

        AssertError(result, ErrorType.Validation, "ProjectEmoji.Required");
    }

    [Fact]
    public void ProjectEmojiCreateReturnsTooLongValidationWhenValueExceedsMaxLength()
    {
        ErrorOr<ProjectEmoji> result = ProjectEmoji.Create(new string('a', ProjectEmoji.MaxLength + 1));

        AssertError(result, ErrorType.Validation, "ProjectEmoji.TooLong");
    }

    [Fact]
    public void ProjectEmojiFromDatabaseThrowsDomainExceptionWhenValueIsBlank()
    {
        Assert.Throws<DomainException>(() => ProjectEmoji.FromDatabase(""));
    }

    [Fact]
    public void ProjectEmojiFromDatabaseThrowsDomainExceptionWhenValueExceedsMaxLength()
    {
        Assert.Throws<DomainException>(() => ProjectEmoji.FromDatabase(new string('a', ProjectEmoji.MaxLength + 1)));
    }

    [Fact]
    public void ProjectInstructionsCreateTrimsWhitespaceWhenValueIsValid()
    {
        ErrorOr<ProjectInstructions> result = ProjectInstructions.Create("  Only finance.  ");

        Assert.False(result.IsError);
        Assert.Equal("Only finance.", result.Value.Value);
        Assert.Equal("Only finance.", result.Value.ToString());
    }

    [Fact]
    public void ProjectInstructionsCreateReturnsRequiredValidationWhenValueIsBlank()
    {
        ErrorOr<ProjectInstructions> result = ProjectInstructions.Create("   ");

        AssertError(result, ErrorType.Validation, "ProjectInstructions.Required");
    }

    [Fact]
    public void ProjectInstructionsCreateReturnsTooLongValidationWhenValueExceedsMaxLength()
    {
        ErrorOr<ProjectInstructions> result = ProjectInstructions.Create(new string('a', ProjectInstructions.MaxLength + 1));

        AssertError(result, ErrorType.Validation, "ProjectInstructions.TooLong");
    }

    [Fact]
    public void ProjectInstructionsFromDatabaseThrowsDomainExceptionWhenValueIsBlank()
    {
        Assert.Throws<DomainException>(() => ProjectInstructions.FromDatabase(""));
    }

    [Fact]
    public void ProjectInstructionsFromDatabaseThrowsDomainExceptionWhenValueIsNotTrimmed()
    {
        Assert.Throws<DomainException>(() => ProjectInstructions.FromDatabase("  padded  "));
    }

    [Fact]
    public void ProjectInstructionsFromDatabaseThrowsDomainExceptionWhenValueExceedsMaxLength()
    {
        Assert.Throws<DomainException>(() => ProjectInstructions.FromDatabase(new string('a', ProjectInstructions.MaxLength + 1)));
    }

    [Fact]
    public void ProjectThemeCreateNormalizesToUppercaseHexWhenValueIsValid()
    {
        ErrorOr<ProjectTheme> result = ProjectTheme.Create("  #f6c543  ");

        Assert.False(result.IsError);
        Assert.Equal("#F6C543", result.Value.Value);
        Assert.Equal("#F6C543", result.Value.ToString());
    }

    [Fact]
    public void ProjectThemeCreateAddsLeadingHashWhenMissing()
    {
        ErrorOr<ProjectTheme> result = ProjectTheme.Create("f6c543");

        Assert.False(result.IsError);
        Assert.Equal("#F6C543", result.Value.Value);
    }

    [Fact]
    public void ProjectThemeCreateReturnsRequiredValidationWhenValueIsBlank()
    {
        ErrorOr<ProjectTheme> result = ProjectTheme.Create("   ");

        AssertError(result, ErrorType.Validation, "ProjectTheme.Required");
    }

    [Fact]
    public void ProjectThemeCreateReturnsInvalidValidationWhenValueIsNotHexColor()
    {
        ErrorOr<ProjectTheme> result = ProjectTheme.Create("not-a-color");

        AssertError(result, ErrorType.Validation, "ProjectTheme.Invalid");
    }

    [Fact]
    public void ProjectThemeFromDatabaseThrowsDomainExceptionWhenValueIsInvalid()
    {
        Assert.Throws<DomainException>(() => ProjectTheme.FromDatabase("not-a-color"));
    }

    private static void AssertError<T>(ErrorOr<T> result, ErrorType type, string code)
    {
        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(type, error.Type);
        Assert.Equal(code, error.Code);
    }
}