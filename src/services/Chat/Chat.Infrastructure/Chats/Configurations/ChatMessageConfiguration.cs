using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.Chats.Configurations;

internal sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("chat_messages");

        builder.HasKey(x => x.Id);

        // Composite alternate key so a shared chat's selected-node FK can reference
        // (chat_id, id), guaranteeing the node belongs to the referenced chat.
        builder.HasAlternateKey(x => new { x.ChatId, x.Id });

        builder.Property(x => x.Id)
            .HasConversion
            (
                id => id.Value,
                value => ChatMessageId.FromDatabase(value)
            )
            .ValueGeneratedNever();

        builder.Property(x => x.ChatId)
            .HasConversion
            (
                id => id.Value,
                value => ChatId.FromDatabase(value)
            )
            .IsRequired();

        builder.Property(x => x.ParentMessageId)
            .HasConversion
            (
                id => id!.Value,
                value => ChatMessageId.FromDatabase(value)
            );

        builder.Property(x => x.Role)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(x => x.Content)
            .HasConversion
            (
                content => content!.Value,
                value => MessageContent.FromDatabase(value)
            )
            .HasMaxLength(MessageContent.MaxLength);

        builder.Property(x => x.LlmModelId)
            .HasConversion
            (
                id => id!.Value,
                value => LlmModelId.FromDatabase(value)
            );

        builder.Property(x => x.FailureReason)
            .HasConversion
            (
                reason => reason!.Value,
                value => FailureReason.FromDatabase(value)
            )
            .HasMaxLength(FailureReason.MaxLength);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.SiblingIndex)
            .HasConversion
            (
                index => index.Value,
                value => SiblingIndex.FromDatabase(value)
            );

        builder.HasOne<ChatMessage>()
            .WithMany()
            .HasForeignKey(x => x.ParentMessageId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.ChatId, x.ParentMessageId, x.SiblingIndex })
            .IsUnique()
            .AreNullsDistinct(false);

        builder.HasIndex(x => x.Status);
    }
}