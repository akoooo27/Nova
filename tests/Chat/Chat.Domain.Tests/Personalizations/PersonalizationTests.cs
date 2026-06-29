using Chat.Domain.Personalizations;
using Chat.Domain.Personalizations.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Domain.Tests.Personalizations;

public sealed class PersonalizationTests
{
    private static readonly UserId User = UserId.FromDatabase("auth0|user-1");

    [Fact]
    public void CreateInitializesPersonalizationWithoutInstructionsOrProfile()
    {
        Personalization personalization = Personalization.Create(User);

        Assert.NotEqual(Guid.Empty, personalization.Id.Value);
        Assert.Equal(User, personalization.UserId);
        Assert.Null(personalization.CustomInstructions);
        Assert.Null(personalization.UserProfile);
    }

    [Fact]
    public void UpdateInstructionsSetsCustomInstructions()
    {
        Personalization personalization = Personalization.Create(User);
        CustomInstructions instructions = CustomInstructions.Create("Be concise.").Value;

        personalization.UpdateInstructions(instructions);

        Assert.Equal(instructions, personalization.CustomInstructions);
    }

    [Fact]
    public void ClearInstructionsResetsCustomInstructionsToNull()
    {
        Personalization personalization = Personalization.Create(User);
        personalization.UpdateInstructions(CustomInstructions.Create("Be concise.").Value);

        personalization.ClearInstructions();

        Assert.Null(personalization.CustomInstructions);
    }

    [Fact]
    public void UpdateUserProfileSetsUserProfile()
    {
        Personalization personalization = Personalization.Create(User);
        UserProfile profile = UserProfile.Create
        (
            name: UserName.Create("Aki").Value,
            role: UserRole.Create("Engineer").Value,
            about: AboutUser.Create("Loves Redis").Value
        );

        personalization.UpdateUserProfile(profile);

        Assert.Equal(profile, personalization.UserProfile);
    }

    [Fact]
    public void ClearUserProfileResetsUserProfileToNull()
    {
        Personalization personalization = Personalization.Create(User);
        personalization.UpdateUserProfile(UserProfile.Create
        (
            name: UserName.Create("Aki").Value,
            role: null,
            about: null
        ));

        personalization.ClearUserProfile();

        Assert.Null(personalization.UserProfile);
    }
}