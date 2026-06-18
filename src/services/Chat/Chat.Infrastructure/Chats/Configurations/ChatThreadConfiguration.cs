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
        builder.ToTable("chats");

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