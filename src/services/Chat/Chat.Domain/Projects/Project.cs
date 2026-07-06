using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;

using SharedKernel;

namespace Chat.Domain.Projects;

public sealed class Project : AggregateRoot<ProjectId>
{
    public UserId UserId { get; private set; } = default!;

    public ProjectName Name { get; private set; } = default!;

    public ProjectInstructions? Instructions { get; private set; } = default!;

    public ProjectEmoji? Emoji { get; private set; } = default!;

    public ProjectTheme? Theme { get; private set; } = default!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private Project()
    {
        // EF Core materialization only
    }

    private Project
    (
        ProjectId id,
        UserId userId,
        ProjectName name,
        ProjectInstructions? instructions,
        ProjectEmoji? emoji,
        ProjectTheme? theme,
        DateTimeOffset createdAt
    ) : base(id)
    {
        UserId = userId;
        Name = name;
        Instructions = instructions;
        Emoji = emoji;
        Theme = theme;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public static Project Create
    (
        UserId userId,
        ProjectName name,
        ProjectInstructions? instructions,
        ProjectEmoji? emoji,
        ProjectTheme? theme,
        DateTimeOffset createdAt
    ) => new
    (
        id: ProjectId.New(),
        userId: userId,
        name: name,
        instructions: instructions,
        emoji: emoji,
        theme: theme,
        createdAt: createdAt
    );

    public void Rename(ProjectName name, DateTimeOffset updatedAt)
    {
        Name = name;
        UpdatedAt = updatedAt;
    }

    public void UpdateInstructions(ProjectInstructions? instructions, DateTimeOffset updatedAt)
    {
        Instructions = instructions;
        UpdatedAt = updatedAt;
    }

    public void UpdateAppearance
    (
        ProjectEmoji? emoji,
        ProjectTheme? theme,
        DateTimeOffset updatedAt
    )
    {
        Emoji = emoji;
        Theme = theme;
        UpdatedAt = updatedAt;
    }
}