using Chat.Domain.FavoriteModels;
using Chat.Domain.FavoriteModels.ValueObjects;
using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.FavoriteModels.Configurations;

internal sealed class FavoriteModelConfiguration : IEntityTypeConfiguration<FavoriteModel>
{
    public void Configure(EntityTypeBuilder<FavoriteModel> builder)
    {
        builder.ToTable("favorite_models");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion
            (
                id => id.Value,
                value => FavoriteModelId.FromDatabase(value)
            )
            .ValueGeneratedNever();

        builder.Property(x => x.UserId)
            .HasConversion
            (
                id => id.Value,
                value => UserId.FromDatabase(value)
            )
            .IsRequired();

        builder.Property(x => x.LlmModelId)
            .HasConversion
            (
                id => id.Value,
                value => LlmModelId.FromDatabase(value)
            )
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => new { x.UserId, x.LlmModelId })
            .IsUnique();

        builder.HasOne<LlmModel>()
            .WithMany()
            .HasForeignKey(x => x.LlmModelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(x => x.DomainEvents);
    }
}