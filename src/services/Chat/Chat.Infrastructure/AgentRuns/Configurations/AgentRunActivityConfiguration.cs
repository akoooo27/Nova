using Chat.Domain.AgentRuns.Entities;
using Chat.Domain.AgentRuns.ValueObjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.AgentRuns.Configurations;

internal sealed class AgentRunActivityConfiguration : IEntityTypeConfiguration<AgentRunActivity>
{
    public void Configure(EntityTypeBuilder<AgentRunActivity> builder)
    {
        builder.ToTable("agent_run_activities");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion
            (
                id => id.Value,
                value => AgentRunActivityId.FromDatabase(value)
            )
            .ValueGeneratedNever();

        builder.Property(x => x.RunId)
            .HasConversion
            (
                id => id.Value,
                value => AgentRunId.FromDatabase(value)
            )
            .IsRequired();

        builder.Property(x => x.Sequence)
            .HasConversion
            (
                sequence => sequence.Value,
                value => ActivitySequence.FromDatabase(value)
            )
            .IsRequired();

        builder.Property(x => x.Kind)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(x => x.Type)
            .HasConversion
            (
                type => type.Value,
                value => ActivityType.FromDatabase(value)
            )
            .HasMaxLength(ActivityType.MaxLength)
            .IsRequired();

        builder.Property(x => x.Title)
            .HasConversion
            (
                title => title.Value,
                value => ActivityTitle.FromDatabase(value)
            )
            .HasMaxLength(ActivityTitle.MaxLength)
            .IsRequired();

        builder.Property(x => x.Detail)
            .HasConversion
            (
                detail => detail!.Value,
                value => ActivityDetail.FromDatabase(value)
            )
            .HasColumnType("jsonb");

        builder.Property(x => x.OccurredAt)
            .IsRequired();

        builder.HasIndex(x => new { x.RunId, x.Sequence })
            .IsUnique();
    }
}