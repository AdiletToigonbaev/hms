using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sprint7_FrontDeskHousekeeping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "housekeeping");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "actual_check_in_at",
                schema: "bookings",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "actual_check_out_at",
                schema: "bookings",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "early_departure_credit",
                schema: "bookings",
                table: "bookings",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "checklist_templates",
                schema: "housekeeping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hotel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    items = table.Column<List<string>>(type: "text[]", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checklist_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_requests",
                schema: "housekeeping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hotel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    severity = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reported_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tasks",
                schema: "housekeeping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hotel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origin = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    priority = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    assigned_to = table.Column<Guid>(type: "uuid", nullable: true),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    inspected_by = table.Column<Guid>(type: "uuid", nullable: true),
                    inspected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reject_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tasks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "task_checklist_items",
                schema: "housekeeping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_done = table.Column<bool>(type: "boolean", nullable: false),
                    done_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_checklist_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_checklist_items_tasks_task_id",
                        column: x => x.task_id,
                        principalSchema: "housekeeping",
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_checklist_templates_hotel_id",
                schema: "housekeeping",
                table: "checklist_templates",
                column: "hotel_id",
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_requests_hotel_id_status_created_at",
                schema: "housekeeping",
                table: "maintenance_requests",
                columns: new[] { "hotel_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_requests_reported_by",
                schema: "housekeeping",
                table: "maintenance_requests",
                column: "reported_by");

            migrationBuilder.CreateIndex(
                name: "ix_task_checklist_items_task_id_position",
                schema: "housekeeping",
                table: "task_checklist_items",
                columns: new[] { "task_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_assigned_to_status",
                schema: "housekeeping",
                table: "tasks",
                columns: new[] { "assigned_to", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_booking_id",
                schema: "housekeeping",
                table: "tasks",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_hotel_id_status",
                schema: "housekeeping",
                table: "tasks",
                columns: new[] { "hotel_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_room_id_created_at",
                schema: "housekeeping",
                table: "tasks",
                columns: new[] { "room_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "checklist_templates",
                schema: "housekeeping");

            migrationBuilder.DropTable(
                name: "maintenance_requests",
                schema: "housekeeping");

            migrationBuilder.DropTable(
                name: "task_checklist_items",
                schema: "housekeeping");

            migrationBuilder.DropTable(
                name: "tasks",
                schema: "housekeeping");

            migrationBuilder.DropColumn(
                name: "actual_check_in_at",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "actual_check_out_at",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "early_departure_credit",
                schema: "bookings",
                table: "bookings");
        }
    }
}
