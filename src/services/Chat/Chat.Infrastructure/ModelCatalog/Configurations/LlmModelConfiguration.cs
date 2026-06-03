using Chat.Application.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.ModelCatalog.ValueObjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.ModelCatalog.Configurations;

internal sealed class LlmModelConfiguration : IEntityTypeConfiguration<LlmModel>
{
    public void Configure(EntityTypeBuilder<LlmModel> builder)
    {
        builder.ToTable("llm_models");

        builder.HasKey(model => model.Id);

        builder.Property(model => model.Id)
            .HasConversion
            (
                id => id.Value,
                value => LlmModelId.FromDatabase(value)
            )
            .ValueGeneratedNever();

        builder.Property(model => model.ProviderId)
            .HasConversion
            (
                id => id.Value,
                value => LlmProviderId.FromDatabase(value)
            )
            .IsRequired();

        builder.Property(model => model.ExternalModelId)
            .HasConversion
            (
                externalModelId => externalModelId.Value,
                value => ExternalModelId.FromDatabase(value)
            )
            .HasMaxLength(256)
            .IsRequired();

        builder.ComplexProperty(model => model.Profile, profile =>
        {
            profile.Property(value => value.Name)
                .HasConversion
                (
                    name => name.Value,
                    value => ModelName.FromDatabase(value)
                )
                .HasColumnName("name")
                .HasMaxLength(ModelCatalogLimits.ModelNameMaxLength)
                .IsRequired();

            profile.Property(value => value.Description)
                .HasConversion
                (
                    description => description.Value,
                    value => ModelDescription.FromDatabase(value)
                )
                .HasColumnName("description")
                .HasMaxLength(ModelCatalogLimits.ModelDescriptionMaxLength)
                .IsRequired();

            profile.Property(value => value.ContextWindow)
                .HasConversion
                (
                    contextWindow => contextWindow.Value,
                    value => ContextWindow.FromDatabase(value)
                )
                .HasColumnName("context_window")
                .IsRequired();

            profile.ComplexProperty(value => value.Capabilities, capabilities =>
            {
                capabilities.Property(value => value.SupportsVision)
                    .HasColumnName("supports_vision")
                    .IsRequired();

                capabilities.Property(value => value.SupportsReasoning)
                    .HasColumnName("supports_reasoning")
                    .IsRequired();

                capabilities.Property(value => value.SupportsToolCalling)
                    .HasColumnName("supports_tool_calling")
                    .IsRequired();
            });
        });

        builder.Property(model => model.SortOrder)
            .HasConversion(
                sortOrder => sortOrder.Value,
                value => SortOrder.FromDatabase(value))
            .IsRequired();

        builder.Property(model => model.IsEnabled)
            .IsRequired();

        builder.HasIndex(model => new { model.ProviderId, model.ExternalModelId })
            .IsUnique();

        builder.HasIndex(model => new { model.ProviderId, model.SortOrder });
    }
}