using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chat.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class ChatSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_chat_messages_chat_id_id",
                table: "chat_messages",
#pragma warning disable CA1861
                columns: new[] { "chat_id", "id" });
#pragma warning restore CA1861

            migrationBuilder.CreateTable(
                name: "shared_chats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    chat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shared_chats", x => x.id);
                    table.ForeignKey(
                        name: "fk_shared_chats_chat_messages_chat_id_current_message_id",
                        columns: x => new { x.chat_id, x.current_message_id },
                        principalTable: "chat_messages",
#pragma warning disable CA1861
                        principalColumns: new[] { "chat_id", "id" },
#pragma warning restore CA1861
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_shared_chats_chats_chat_id",
                        column: x => x.chat_id,
                        principalTable: "chats",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shared_chats_chat_id_current_message_id",
                table: "shared_chats",
#pragma warning disable CA1861
                columns: new[] { "chat_id", "current_message_id" },
#pragma warning restore CA1861
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shared_chats_user_id_created_at_id",
                table: "shared_chats",
#pragma warning disable CA1861
                columns: new[] { "user_id", "created_at", "id" },
#pragma warning restore CA1861
#pragma warning disable CA1861
                descending: new[] { false, true, true });
#pragma warning restore CA1861
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shared_chats");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_chat_messages_chat_id_id",
                table: "chat_messages");
        }
    }
}