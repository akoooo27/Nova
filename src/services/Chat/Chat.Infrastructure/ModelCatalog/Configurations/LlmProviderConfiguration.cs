using Chat.Application.ModelCatalog;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.ModelCatalog.Configurations;

internal sealed class LlmProviderConfiguration : IEntityTypeConfiguration<LlmProvider>
{
    public void Configure(EntityTypeBuilder<LlmProvider> builder)
    {
        builder.ToTable("llm_providers");

        builder.HasKey(provider => provider.Id);

        builder.Property(provider => provider.Id)
            .HasConversion
            (
                id => id.Value,
                value => LlmProviderId.FromDatabase(value)
            )
            .ValueGeneratedNever();

        builder.Property(provider => provider.Name)
            .HasConversion
            (
                name => name.Value,
                value => ProviderName.FromDatabase(value)
            )
            .HasMaxLength(ModelCatalogLimits.ProviderNameMaxLength)
            .IsRequired();

        builder.Property(provider => provider.Slug)
            .HasConversion
            (
                slug => slug.Value,
                value => ProviderSlug.FromDatabase(value)
            )
            .HasMaxLength(ModelCatalogLimits.ProviderSlugMaxLength)
            .IsRequired();

        builder.Property(provider => provider.SortOrder)
            .HasConversion
            (
                sortOrder => sortOrder.Value,
                value => SortOrder.FromDatabase(value)
            )
            .IsRequired();

        builder.Property(provider => provider.LogoKey)
            .HasConversion
            (
                logoKey => logoKey == null ? null : logoKey.Value,
                value => value == null ? null : AssetKey.Create(value).Value
            )
            .HasMaxLength(ModelCatalogLimits.ProviderLogoKeyMaxLength);

        builder.HasIndex(provider => provider.Slug)
            .IsUnique();

        builder.HasIndex(provider => new { provider.SortOrder, provider.Name });

        builder.HasMany(provider => provider.Models)
            .WithOne()
            .HasForeignKey(model => model.ProviderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(provider => provider.Models)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(provider => provider.DomainEvents);
    }
}