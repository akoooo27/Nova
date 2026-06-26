using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;
using Chat.Domain.SharedChats;
using Chat.Domain.SharedChats.ValueObjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.SharedChats.Configurations;

internal sealed class SharedChatConfiguration : IEntityTypeConfiguration<SharedChat>
{
    public void Configure(EntityTypeBuilder<SharedChat> builder)
    {
        builder.ToTable("shared_chats");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion
            (
                id => id.Value,
                value => SharedChatId.FromDatabase(value)
            )
            .ValueGeneratedNever();

        builder.Property(x => x.UserId)
            .HasConversion
            (
                id => id.Value,
                value => UserId.FromDatabase(value)
            )
            .IsRequired();

        builder.Property(x => x.ChatId)
            .HasConversion
            (
                id => id.Value,
                value => ChatId.FromDatabase(value)
            )
            .IsRequired();

        builder.Property(x => x.CurrentMessageId)
            .HasConversion
            (
                id => id.Value,
                value => ChatMessageId.FromDatabase(value)
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

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => new { x.ChatId, x.CurrentMessageId })
            .IsUnique();

        builder.HasIndex(x => new { x.UserId, x.CreatedAt, x.Id })
            .IsDescending(false, true, true);

        builder.HasOne<ChatThread>()
            .WithMany()
            .HasForeignKey(x => x.ChatId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ChatMessage>()
            .WithMany()
            .HasForeignKey(x => new { x.ChatId, x.CurrentMessageId })
            .HasPrincipalKey(x => new { x.ChatId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(x => x.DomainEvents);
    }
}