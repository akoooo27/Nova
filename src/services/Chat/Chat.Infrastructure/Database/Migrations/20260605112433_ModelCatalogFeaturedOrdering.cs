using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chat.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class ModelCatalogFeaturedOrdering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_llm_providers_sort_order_name",
                table: "llm_providers");

            migrationBuilder.DropIndex(
                name: "ix_llm_models_provider_id_sort_order",
                table: "llm_models");

            migrationBuilder.DropColumn(
                name: "sort_order",
                table: "llm_providers");

            migrationBuilder.DropColumn(
                name: "sort_order",
                table: "llm_models");

            migrationBuilder.AddColumn<bool>(
                name: "is_featured",
                table: "llm_providers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_llm_providers_is_featured_name",
                table: "llm_providers",
#pragma warning disable CA1861
                columns: new[] { "is_featured", "name" },
                descending: new[] { true, false });
#pragma warning restore CA1861
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_llm_providers_is_featured_name",
                table: "llm_providers");

            migrationBuilder.DropColumn(
                name: "is_featured",
                table: "llm_providers");

            migrationBuilder.AddColumn<int>(
                name: "sort_order",
                table: "llm_providers",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "sort_order",
                table: "llm_models",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "ix_llm_providers_sort_order_name",
                table: "llm_providers",
#pragma warning disable CA1861
                columns: new[] { "sort_order", "name" });
#pragma warning restore CA1861

            migrationBuilder.CreateIndex(
                name: "ix_llm_models_provider_id_sort_order",
                table: "llm_models",
#pragma warning disable CA1861
                columns: new[] { "provider_id", "sort_order" });
#pragma warning restore CA1861
        }
    }
}