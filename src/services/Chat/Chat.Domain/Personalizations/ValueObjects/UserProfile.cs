namespace Chat.Domain.Personalizations.ValueObjects;

public sealed record UserProfile
{
    public UserName? Name { get; private set; }

    public UserRole? Role { get; private set; }

    public AboutUser? About { get; private set; }

    private UserProfile()
    {
        // For EF Core
    }

    private UserProfile
    (
        UserName? name,
        UserRole? role,
        AboutUser? about
    )
    {
        Name = name;
        Role = role;
        About = about;
    }

    public static UserProfile Create
    (
        UserName? name,
        UserRole? role,
        AboutUser? about
    ) => new
    (
        name: name,
        role: role,
        about: about
    );
}