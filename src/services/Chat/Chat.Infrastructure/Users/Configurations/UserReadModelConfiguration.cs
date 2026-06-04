using Chat.Infrastructure.Users.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.Users.Configurations;

internal sealed class UserReadModelConfiguration : IEntityTypeConfiguration<UserReadModel>
{
    public void Configure(EntityTypeBuilder<UserReadModel> builder)
    {
        builder.ToTable("users");

        builder.HasKey(user => new { user.Provider, user.ProviderUserId });

        builder.Property(user => user.ProviderUserId)
            .HasMaxLength(UserReadModelLimits.ProviderUserIdMaxLength)
            .IsRequired();

        builder.Property(user => user.Provider)
            .HasMaxLength(UserReadModelLimits.ProviderMaxLength)
            .IsRequired();

        builder.Property(user => user.Email)
            .HasMaxLength(UserReadModelLimits.EmailMaxLength);

        builder.Property(user => user.Name)
            .HasMaxLength(UserReadModelLimits.NameMaxLength);

        builder.Property(user => user.IsDeleted)
            .IsRequired();
    }
}