using System;

using Chat.Infrastructure.Database;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chat.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ChatDbContext))]
    [Migration("20260614161000_ChatPinArchive")]
    public partial class ChatPinArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_archived",
                table: "chats",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pinned_at",
                table: "chats",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_archived",
                table: "chats");

            migrationBuilder.DropColumn(
                name: "pinned_at",
                table: "chats");
        }
    }
}
