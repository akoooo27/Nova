using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chat.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedChatRemix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "allow_remix",
                table: "shared_chats",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "remixed_from_chat_id",
                table: "chats",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "remixed_from_message_id",
                table: "chats",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "remixed_from_share_id",
                table: "chats",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_chats_remix_origin_complete",
                table: "chats",
                sql: "(remixed_from_share_id is null) = (remixed_from_chat_id is null) and (remixed_from_share_id is null) = (remixed_from_message_id is null)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_chats_remix_origin_complete",
                table: "chats");

            migrationBuilder.DropColumn(
                name: "allow_remix",
                table: "shared_chats");

            migrationBuilder.DropColumn(
                name: "remixed_from_chat_id",
                table: "chats");

            migrationBuilder.DropColumn(
                name: "remixed_from_message_id",
                table: "chats");

            migrationBuilder.DropColumn(
                name: "remixed_from_share_id",
                table: "chats");
        }
    }
}