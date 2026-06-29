using Chat.Domain.Personalizations.ValueObjects;
using Chat.Domain.Shared;

using SharedKernel;

namespace Chat.Domain.Personalizations;

public sealed class Personalization : AggregateRoot<PersonalizationId>
{
    public UserId UserId { get; private set; } = default!;

    public CustomInstructions? CustomInstructions { get; private set; }

    public UserProfile? UserProfile { get; private set; }

    private Personalization()
    {
        // EF Core materialization only
    }

    public Personalization
    (
        PersonalizationId id,
        UserId userId
    ) : base(id)
    {
        UserId = userId;
        CustomInstructions = null;
        UserProfile = null;
    }

    public static Personalization Create(UserId userId) =>
        new(PersonalizationId.New(), userId);

    public void UpdateInstructions(CustomInstructions instructions) =>
        CustomInstructions = instructions;

    public void ClearInstructions() =>
        CustomInstructions = null;

    public void UpdateUserProfile(UserProfile userProfile) =>
        UserProfile = userProfile;

    public void ClearUserProfile() =>
        UserProfile = null;
}