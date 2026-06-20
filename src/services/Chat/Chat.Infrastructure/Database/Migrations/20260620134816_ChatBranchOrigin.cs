using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chat.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class ChatBranchOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "branched_from_chat_id",
                table: "chats",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "branched_from_message_id",
                table: "chats",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_chats_branch_origin_complete",
                table: "chats",
                sql: "(branched_from_chat_id is null) = (branched_from_message_id is null)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_chats_branch_origin_complete",
                table: "chats");

            migrationBuilder.DropColumn(
                name: "branched_from_chat_id",
                table: "chats");

            migrationBuilder.DropColumn(
                name: "branched_from_message_id",
                table: "chats");
        }
    }
}