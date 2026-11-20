using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sprint2_HotelFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "hotel");

            migrationBuilder.CreateTable(
                name: "hotels",
                schema: "hotel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hotels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stored_files",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    original_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    visibility = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stored_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hotel_settings",
                schema: "hotel",
                columns: table => new
                {
                    hotel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    check_in_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    check_out_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    online_payment_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    prepayment_percent = table.Column<int>(type: "integer", nullable: false),
                    payment_hold_minutes = table.Column<int>(type: "integer", nullable: false),
                    pending_hold_hours = table.Column<int>(type: "integer", nullable: false),
                    auto_confirm_web_bookings = table.Column<bool>(type: "boolean", nullable: false),
                    auto_release_room_after_cleaning = table.Column<bool>(type: "boolean", nullable: false),
                    require_email_confirmation = table.Column<bool>(type: "boolean", nullable: false),
                    no_show_cutoff_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    rules = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    cancellation_policy = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hotel_settings", x => x.hotel_id);
                    table.CheckConstraint("ck_hotel_settings_holds", "payment_hold_minutes > 0 AND pending_hold_hours > 0");
                    table.CheckConstraint("ck_hotel_settings_prepayment", "prepayment_percent BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "fk_hotel_settings_hotels_hotel_id",
                        column: x => x.hotel_id,
                        principalSchema: "hotel",
                        principalTable: "hotels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "integration_settings",
                schema: "hotel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hotel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    public_config = table.Column<Dictionary<string, string>>(type: "jsonb", nullable: false),
                    last_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_check_status = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_settings", x => x.id);
                    table.ForeignKey(
                        name: "fk_integration_settings_hotels_hotel_id",
                        column: x => x.hotel_id,
                        principalSchema: "hotel",
                        principalTable: "hotels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_integration_settings_hotel_id_provider",
                schema: "hotel",
                table: "integration_settings",
                columns: new[] { "hotel_id", "provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stored_files_path",
                schema: "platform",
                table: "stored_files",
                column: "path",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hotel_settings",
                schema: "hotel");

            migrationBuilder.DropTable(
                name: "integration_settings",
                schema: "hotel");

            migrationBuilder.DropTable(
                name: "stored_files",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "hotels",
                schema: "hotel");
        }
    }
}
