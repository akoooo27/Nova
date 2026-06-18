using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861
namespace Chat.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class ChatTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    current_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chats", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    role = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "character varying(32768)", maxLength: 32768, nullable: true),
                    llm_model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sibling_index = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_messages_chat_messages_parent_message_id",
                        column: x => x.parent_message_id,
                        principalTable: "chat_messages",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_chat_messages_chats_chat_id",
                        column: x => x.chat_id,
                        principalTable: "chats",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_chat_id_parent_message_id_sibling_index",
                table: "chat_messages",
                columns: new[] { "chat_id", "parent_message_id", "sibling_index" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_parent_message_id",
                table: "chat_messages",
                column: "parent_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_status",
                table: "chat_messages",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_chats_user_id_updated_at_id",
                table: "chats",
                columns: new[] { "user_id", "updated_at", "id" },
                descending: new[] { false, true, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_messages");

            migrationBuilder.DropTable(
                name: "chats");
        }
    }
}