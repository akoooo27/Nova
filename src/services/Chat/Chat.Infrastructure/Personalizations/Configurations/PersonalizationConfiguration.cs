using Chat.Domain.Personalizations;
using Chat.Domain.Personalizations.ValueObjects;
using Chat.Domain.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.Personalizations.Configurations;

internal sealed class PersonalizationConfiguration : IEntityTypeConfiguration<Personalization>
{
    public void Configure(EntityTypeBuilder<Personalization> builder)
    {
        builder.ToTable("personalizations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion
            (
                id => id.Value,
                value => PersonalizationId.FromDatabase(value)
            )
            .ValueGeneratedNever();

        builder.Property(x => x.UserId)
            .HasConversion
            (
                id => id.Value,
                value => UserId.FromDatabase(value)
            )
            .IsRequired();

        builder.Property(x => x.CustomInstructions)
            .HasConversion
            (
                instructions => instructions == null ? null : instructions.Value,
                value => value == null ? null : CustomInstructions.FromDatabase(value)
            )
            .HasMaxLength(CustomInstructions.MaxLength);

        builder.ComplexProperty(x => x.UserProfile, profile =>
        {
            profile.IsRequired(false);

            // All UserProfile members are optional, so EF needs a shadow discriminator
            // to distinguish "no profile" from "profile present with all fields blank".
            profile.HasDiscriminator();

            profile.Property(p => p.Name)
                .HasConversion
                (
                    name => name == null ? null : name.Value,
                    value => value == null ? null : UserName.FromDatabase(value)
                )
                .HasColumnName("user_name")
                .HasMaxLength(UserName.MaxLength);

            profile.Property(p => p.Role)
                .HasConversion
                (
                    role => role == null ? null : role.Value,
                    value => value == null ? null : UserRole.FromDatabase(value)
                )
                .HasColumnName("user_role")
                .HasMaxLength(UserRole.MaxLength);

            profile.Property(p => p.About)
                .HasConversion
                (
                    about => about == null ? null : about.Value,
                    value => value == null ? null : AboutUser.FromDatabase(value)
                )
                .HasColumnName("about_user")
                .HasMaxLength(AboutUser.MaxLength);
        });

        builder.HasIndex(x => x.UserId)
            .IsUnique();

        builder.Ignore(x => x.DomainEvents);
    }
}