using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chat.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class FavoriteModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "favorite_models",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    llm_model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_favorite_models", x => x.id);
                    table.ForeignKey(
                        name: "fk_favorite_models_llm_models_llm_model_id",
                        column: x => x.llm_model_id,
                        principalTable: "llm_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_favorite_models_llm_model_id",
                table: "favorite_models",
                column: "llm_model_id");

            migrationBuilder.CreateIndex(
                name: "ix_favorite_models_user_id_llm_model_id",
                table: "favorite_models",
#pragma warning disable CA1861
                columns: new[] { "user_id", "llm_model_id" },
#pragma warning restore CA1861
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "favorite_models");
        }
    }
}
