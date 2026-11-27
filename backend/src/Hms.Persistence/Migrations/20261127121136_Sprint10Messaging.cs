using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sprint10Messaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "messaging");

            migrationBuilder.CreateTable(
                name: "auto_replies",
                schema: "messaging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hotel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    keywords = table.Column<string[]>(type: "text[]", nullable: false),
                    body = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                    action = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auto_replies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conversations",
                schema: "messaging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hotel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    guest_id = table.Column<Guid>(type: "uuid", nullable: true),
                    profile_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    last_inbound_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    needs_operator = table.Column<bool>(type: "boolean", nullable: false),
                    unread_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                schema: "messaging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hotel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guest_id = table.Column<Guid>(type: "uuid", nullable: true),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    direction = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    template_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    recipient = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    body = table.Column<string>(type: "text", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    whats_app_template = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    whats_app_language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    parameters = table.Column<string>(type: "jsonb", nullable: true),
                    parameter_names = table.Column<string[]>(type: "text[]", nullable: true),
                    is_authentication = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    dedup_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    email_fallback = table.Column<bool>(type: "boolean", nullable: false),
                    fallback_for = table.Column<Guid>(type: "uuid", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    sent_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "phone_verification_codes",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_phone_verification_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_phone_verification_codes_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                schema: "messaging",
                columns: table => new
                {
                    hotel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hourly_start_reminder_minutes = table.Column<int>(type: "integer", nullable: false),
                    hourly_end_reminder_minutes = table.Column<int>(type: "integer", nullable: false),
                    nightly_start_reminder_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    nightly_end_reminder_minutes = table.Column<int>(type: "integer", nullable: false),
                    auto_replies_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    greeting_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    fallback_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    stop_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settings", x => x.hotel_id);
                });

            migrationBuilder.CreateTable(
                name: "templates",
                schema: "messaging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hotel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    email_subject = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    email_body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    whats_app_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    whats_app_body = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    whats_app_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_events",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    dedup_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_auto_replies_hotel_id_number",
                schema: "messaging",
                table: "auto_replies",
                columns: new[] { "hotel_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversations_guest_id",
                schema: "messaging",
                table: "conversations",
                column: "guest_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_hotel_id_last_message_at",
                schema: "messaging",
                table: "conversations",
                columns: new[] { "hotel_id", "last_message_at" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_hotel_id_phone",
                schema: "messaging",
                table: "conversations",
                columns: new[] { "hotel_id", "phone" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_messages_booking_id",
                schema: "messaging",
                table: "messages",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_messages_conversation_id_created_at",
                schema: "messaging",
                table: "messages",
                columns: new[] { "conversation_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_guest_id_created_at",
                schema: "messaging",
                table: "messages",
                columns: new[] { "guest_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_hotel_id_created_at",
                schema: "messaging",
                table: "messages",
                columns: new[] { "hotel_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_hotel_id_dedup_key",
                schema: "messaging",
                table: "messages",
                columns: new[] { "hotel_id", "dedup_key" },
                unique: true,
                filter: "dedup_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_messages_hotel_id_external_id",
                schema: "messaging",
                table: "messages",
                columns: new[] { "hotel_id", "external_id" },
                unique: true,
                filter: "external_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_phone_verification_codes_user_id_created_at",
                schema: "identity",
                table: "phone_verification_codes",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_templates_hotel_id_code_language",
                schema: "messaging",
                table: "templates",
                columns: new[] { "hotel_id", "code", "language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_received_at",
                schema: "platform",
                table: "webhook_events",
                column: "received_at");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_source_dedup_key",
                schema: "platform",
                table: "webhook_events",
                columns: new[] { "source", "dedup_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auto_replies",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "conversations",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "messages",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "phone_verification_codes",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "settings",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "templates",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "webhook_events",
                schema: "platform");
        }
    }
}
