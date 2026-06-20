using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.Chats.Configurations;

internal sealed class ChatThreadConfiguration : IEntityTypeConfiguration<ChatThread>
{
    public void Configure(EntityTypeBuilder<ChatThread> builder)
    {
        builder.ToTable("chats", table => table.HasCheckConstraint
        (
            "ck_chats_branch_origin_complete",
            "(branched_from_chat_id is null) = (branched_from_message_id is null)"
        ));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion
            (
                id => id.Value,
                value => ChatId.FromDatabase(value)
            )
            .ValueGeneratedNever();

        builder.Property(x => x.UserId)
            .HasConversion
            (
                id => id.Value,
                value => UserId.FromDatabase(value)
            )
            .IsRequired();

        builder.Property(x => x.Title)
            .HasConversion
            (
                title => title.Value,
                value => ChatTitle.FromDatabase(value)
            )
            .HasMaxLength(ChatTitle.MaxLength)
            .IsRequired();

        builder.Property(x => x.CurrentMessageId)
            .HasConversion
            (
                id => id.Value,
                value => ChatMessageId.FromDatabase(value)
            )
            .IsRequired();

        builder.ComplexProperty(x => x.BranchOrigin, origin =>
        {
            origin.IsRequired(false);

            origin.Property(value => value.SourceChatId)
                .HasConversion
                (
                    id => id.Value,
                    value => ChatId.FromDatabase(value)
                )
                .HasColumnName("branched_from_chat_id");

            origin.Property(value => value.SourceMessageId)
                .HasConversion
                (
                    id => id.Value,
                    value => ChatMessageId.FromDatabase(value)
                )
                .HasColumnName("branched_from_message_id");
        });

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .IsRequired();

        builder.Property(x => x.PinnedAt);

        builder.Property(x => x.IsTemporary)
            .IsRequired();

        builder.Property(x => x.IsArchived)
            .IsRequired();

        builder.Ignore(x => x.IsPinned);

        builder.Property<uint>("xmin")
            .IsRowVersion();

        builder.HasMany(x => x.Messages)
            .WithOne()
            .HasForeignKey(message => message.ChatId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Messages)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.UserId, x.UpdatedAt, x.Id })
            .IsDescending(false, true, false);

        builder.Ignore(x => x.DomainEvents);
    }
}