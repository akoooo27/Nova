using Chat.Domain.Projects;
using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.Projects.Configurations;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion
            (
                id => id.Value,
                value => ProjectId.FromDatabase(value)
            )
            .ValueGeneratedNever();

        builder.Property(x => x.UserId)
            .HasConversion
            (
                id => id.Value,
                value => UserId.FromDatabase(value)
            )
            .IsRequired();

        builder.Property(x => x.Name)
            .HasConversion
            (
                name => name.Value,
                value => ProjectName.FromDatabase(value)
            )
            .HasMaxLength(ProjectName.MaxLength)
            .IsRequired();

        builder.Property(x => x.Instructions)
            .HasConversion
            (
                instructions => instructions == null ? null : instructions.Value,
                value => value == null ? null : ProjectInstructions.FromDatabase(value)
            )
            .HasMaxLength(ProjectInstructions.MaxLength);

        builder.Property(x => x.Emoji)
            .HasConversion
            (
                emoji => emoji == null ? null : emoji.Value,
                value => value == null ? null : ProjectEmoji.FromDatabase(value)
            )
            .HasMaxLength(ProjectEmoji.MaxLength);

        builder.Property(x => x.Theme)
            .HasConversion
            (
                theme => theme == null ? null : theme.Value,
                value => value == null ? null : ProjectTheme.FromDatabase(value)
            );

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .IsRequired();

        builder.HasIndex(x => new { x.UserId, x.UpdatedAt, x.Id })
            .IsDescending(false, true, false);

        builder.Ignore(x => x.DomainEvents);
    }
}