using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chat.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class ModelCatalogUpdateTableNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_llm_model_llm_providers_provider_id",
                table: "llm_model");

            migrationBuilder.DropPrimaryKey(
                name: "pk_llm_model",
                table: "llm_model");

            migrationBuilder.RenameTable(
                name: "llm_model",
                newName: "llm_models");

            migrationBuilder.RenameIndex(
                name: "ix_llm_model_provider_id_sort_order",
                table: "llm_models",
                newName: "ix_llm_models_provider_id_sort_order");

            migrationBuilder.RenameIndex(
                name: "ix_llm_model_provider_id_external_model_id",
                table: "llm_models",
                newName: "ix_llm_models_provider_id_external_model_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_llm_models",
                table: "llm_models",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_llm_models_llm_providers_provider_id",
                table: "llm_models",
                column: "provider_id",
                principalTable: "llm_providers",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_llm_models_llm_providers_provider_id",
                table: "llm_models");

            migrationBuilder.DropPrimaryKey(
                name: "pk_llm_models",
                table: "llm_models");

            migrationBuilder.RenameTable(
                name: "llm_models",
                newName: "llm_model");

            migrationBuilder.RenameIndex(
                name: "ix_llm_models_provider_id_sort_order",
                table: "llm_model",
                newName: "ix_llm_model_provider_id_sort_order");

            migrationBuilder.RenameIndex(
                name: "ix_llm_models_provider_id_external_model_id",
                table: "llm_model",
                newName: "ix_llm_model_provider_id_external_model_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_llm_model",
                table: "llm_model",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_llm_model_llm_providers_provider_id",
                table: "llm_model",
                column: "provider_id",
                principalTable: "llm_providers",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}