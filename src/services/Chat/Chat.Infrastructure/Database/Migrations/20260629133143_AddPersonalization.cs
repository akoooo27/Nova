using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chat.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "personalizations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    custom_instructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    about_user = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: true),
                    user_profile_discriminator = table.Column<string>(type: "text", nullable: true),
                    user_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    user_role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personalizations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_personalizations_user_id",
                table: "personalizations",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "personalizations");
        }
    }
}