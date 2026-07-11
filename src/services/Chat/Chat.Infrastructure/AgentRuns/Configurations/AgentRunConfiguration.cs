using Chat.Domain.AgentRuns;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.AgentRuns.Configurations;

internal sealed class AgentRunConfiguration : IEntityTypeConfiguration<AgentRun>
{
    public void Configure(EntityTypeBuilder<AgentRun> builder)
    {
        builder.ToTable("agent_runs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion
            (
                id => id.Value,
                value => AgentRunId.FromDatabase(value)
            )
            .ValueGeneratedNever();

        builder.Property(x => x.ChatId)
            .HasConversion
            (
                id => id.Value,
                value => ChatId.FromDatabase(value)
            )
            .IsRequired();

        builder.Property(x => x.AssistantMessageId)
            .HasConversion
            (
                id => id.Value,
                value => ChatMessageId.FromDatabase(value)
            )
            .IsRequired();

        builder.Property(x => x.UserId)
            .HasConversion
            (
                id => id.Value,
                value => UserId.FromDatabase(value)
            )
            .IsRequired();

        builder.Property(x => x.Kind)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(x => x.Task)
            .HasConversion
            (
                task => task.Value,
                value => AgentTask.FromDatabase(value)
            )
            .HasMaxLength(AgentTask.MaxLength)
            .IsRequired();

        builder.Property(x => x.LlmModelId)
            .HasConversion
            (
                id => id.Value,
                value => LlmModelId.FromDatabase(value)
            )
            .IsRequired();

        builder.ComplexProperty(x => x.Usage, usage =>
        {
            usage.Property(value => value.InputTokens)
                .HasColumnName("input_tokens");

            usage.Property(value => value.OutputTokens)
                .HasColumnName("output_tokens");
        });

        builder.Property(x => x.StartedAt)
            .IsRequired();

        builder.Property(x => x.FinishedAt);

        builder.Ignore(x => x.CurrentPhase);

        builder.HasIndex(x => x.AssistantMessageId)
            .IsUnique();

        builder.HasOne<ChatThread>()
            .WithMany()
            .HasForeignKey(x => x.ChatId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ChatMessage>()
            .WithMany()
            .HasForeignKey(x => new { x.ChatId, x.AssistantMessageId })
            .HasPrincipalKey(x => new { x.ChatId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Activities)
            .WithOne()
            .HasForeignKey(activity => activity.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Activities)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(x => x.DomainEvents);
    }
}