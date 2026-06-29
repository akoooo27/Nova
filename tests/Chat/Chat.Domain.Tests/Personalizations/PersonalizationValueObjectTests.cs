using Chat.Domain;
using Chat.Domain.Personalizations.ValueObjects;

using ErrorOr;

namespace Chat.Domain.Tests.Personalizations;

public sealed class PersonalizationValueObjectTests
{
    [Fact]
    public void CustomInstructionsCreateTrimsWhitespaceWhenValueIsValid()
    {
        ErrorOr<CustomInstructions> result = CustomInstructions.Create("  Be concise  ");

        Assert.False(result.IsError);
        Assert.Equal("Be concise", result.Value.Value);
        Assert.Equal("Be concise", result.Value.ToString());
    }

    [Fact]
    public void CustomInstructionsCreateReturnsRequiredValidationWhenValueIsBlank()
    {
        ErrorOr<CustomInstructions> result = CustomInstructions.Create("   ");

        AssertError(result, ErrorType.Validation, "CustomInstructions.Required");
    }

    [Fact]
    public void CustomInstructionsCreateReturnsTooLongValidationWhenValueExceedsMaxLength()
    {
        ErrorOr<CustomInstructions> result = CustomInstructions.Create(new string('a', CustomInstructions.MaxLength + 1));

        AssertError(result, ErrorType.Validation, "CustomInstructions.TooLong");
    }

    [Fact]
    public void CustomInstructionsFromDatabaseThrowsDomainExceptionWhenValueIsBlank()
    {
        Assert.Throws<DomainException>(() => CustomInstructions.FromDatabase(""));
    }

    [Fact]
    public void CustomInstructionsFromDatabaseThrowsDomainExceptionWhenValueExceedsMaxLength()
    {
        Assert.Throws<DomainException>(() => CustomInstructions.FromDatabase(new string('a', CustomInstructions.MaxLength + 1)));
    }

    [Fact]
    public void UserNameCreateTrimsWhitespaceWhenValueIsValid()
    {
        ErrorOr<UserName> result = UserName.Create("  Aki  ");

        Assert.False(result.IsError);
        Assert.Equal("Aki", result.Value.Value);
        Assert.Equal("Aki", result.Value.ToString());
    }

    [Fact]
    public void UserNameCreateReturnsRequiredValidationWhenValueIsBlank()
    {
        ErrorOr<UserName> result = UserName.Create("   ");

        AssertError(result, ErrorType.Validation, "UserName.Required");
    }

    [Fact]
    public void UserNameCreateReturnsTooLongValidationWhenValueExceedsMaxLength()
    {
        ErrorOr<UserName> result = UserName.Create(new string('a', UserName.MaxLength + 1));

        AssertError(result, ErrorType.Validation, "UserName.TooLong");
    }

    [Fact]
    public void UserNameFromDatabaseThrowsDomainExceptionWhenValueExceedsMaxLength()
    {
        Assert.Throws<DomainException>(() => UserName.FromDatabase(new string('a', UserName.MaxLength + 1)));
    }

    [Fact]
    public void UserRoleCreateTrimsWhitespaceWhenValueIsValid()
    {
        ErrorOr<UserRole> result = UserRole.Create("  Engineer  ");

        Assert.False(result.IsError);
        Assert.Equal("Engineer", result.Value.Value);
        Assert.Equal("Engineer", result.Value.ToString());
    }

    [Fact]
    public void UserRoleCreateReturnsRequiredValidationWhenValueIsBlank()
    {
        ErrorOr<UserRole> result = UserRole.Create("   ");

        AssertError(result, ErrorType.Validation, "UserRole.Required");
    }

    [Fact]
    public void UserRoleCreateReturnsTooLongValidationWhenValueExceedsMaxLength()
    {
        ErrorOr<UserRole> result = UserRole.Create(new string('a', UserRole.MaxLength + 1));

        AssertError(result, ErrorType.Validation, "UserRole.TooLong");
    }

    [Fact]
    public void UserRoleFromDatabaseThrowsDomainExceptionWhenValueIsBlank()
    {
        Assert.Throws<DomainException>(() => UserRole.FromDatabase(""));
    }

    [Fact]
    public void AboutUserCreateTrimsWhitespaceWhenValueIsValid()
    {
        ErrorOr<AboutUser> result = AboutUser.Create("  Loves Redis  ");

        Assert.False(result.IsError);
        Assert.Equal("Loves Redis", result.Value.Value);
        Assert.Equal("Loves Redis", result.Value.ToString());
    }

    [Fact]
    public void AboutUserCreateReturnsRequiredValidationWhenValueIsBlank()
    {
        ErrorOr<AboutUser> result = AboutUser.Create("   ");

        AssertError(result, ErrorType.Validation, "AboutUser.Required");
    }

    [Fact]
    public void AboutUserCreateReturnsTooLongValidationWhenValueExceedsMaxLength()
    {
        ErrorOr<AboutUser> result = AboutUser.Create(new string('a', AboutUser.MaxLength + 1));

        AssertError(result, ErrorType.Validation, "AboutUser.TooLong");
    }

    [Fact]
    public void AboutUserFromDatabaseThrowsDomainExceptionWhenValueExceedsMaxLength()
    {
        Assert.Throws<DomainException>(() => AboutUser.FromDatabase(new string('a', AboutUser.MaxLength + 1)));
    }

    [Fact]
    public void UserProfileCreateKeepsEachProvidedField()
    {
        UserProfile profile = UserProfile.Create
        (
            name: UserName.Create("Aki").Value,
            role: UserRole.Create("Engineer").Value,
            about: AboutUser.Create("Loves Redis").Value
        );

        Assert.Equal("Aki", profile.Name!.Value);
        Assert.Equal("Engineer", profile.Role!.Value);
        Assert.Equal("Loves Redis", profile.About!.Value);
    }

    [Fact]
    public void UserProfileCreateAllowsNullFields()
    {
        UserProfile profile = UserProfile.Create
        (
            name: UserName.Create("Aki").Value,
            role: null,
            about: null
        );

        Assert.Equal("Aki", profile.Name!.Value);
        Assert.Null(profile.Role);
        Assert.Null(profile.About);
    }

    private static void AssertError<T>(ErrorOr<T> result, ErrorType type, string code)
    {
        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(type, error.Type);
        Assert.Equal(code, error.Code);
    }
}