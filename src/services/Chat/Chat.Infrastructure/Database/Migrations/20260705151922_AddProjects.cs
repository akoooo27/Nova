using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chat.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "project_id",
                table: "chats",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    instructions = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    emoji = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    theme = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projects", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chats_project_id",
                table: "chats",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_chats_user_id_project_id",
                table: "chats",
#pragma warning disable CA1861
                columns: new[] { "user_id", "project_id" });
#pragma warning restore CA1861

            migrationBuilder.CreateIndex(
                name: "ix_projects_user_id_updated_at_id",
                table: "projects",
#pragma warning disable CA1861
                columns: new[] { "user_id", "updated_at", "id" },
#pragma warning restore CA1861
#pragma warning disable CA1861
                descending: new[] { false, true, false });
#pragma warning restore CA1861

            migrationBuilder.AddForeignKey(
                name: "fk_chats_projects_project_id",
                table: "chats",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_chats_projects_project_id",
                table: "chats");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropIndex(
                name: "ix_chats_project_id",
                table: "chats");

            migrationBuilder.DropIndex(
                name: "ix_chats_user_id_project_id",
                table: "chats");

            migrationBuilder.DropColumn(
                name: "project_id",
                table: "chats");
        }
    }
}