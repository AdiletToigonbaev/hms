using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sprint5_CalendarRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auto_no_show_enabled",
                schema: "hotel",
                table: "hotel_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "hold_until",
                schema: "bookings",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "penalty_amount",
                schema: "bookings",
                table: "bookings",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "penalty_kind",
                schema: "bookings",
                table: "bookings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "penalty_waived",
                schema: "bookings",
                table: "bookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_bookings_hold_until",
                schema: "bookings",
                table: "bookings",
                column: "hold_until",
                filter: "hold_until IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bookings_penalty",
                schema: "bookings",
                table: "bookings",
                sql: "penalty_amount >= 0");

            // Неподтверждённые брони, созданные до Sprint 5, получают срок удержания по текущим настройкам:
            // от момента создания (почасовые — PendingHoldMinutes, посуточные — PendingHoldHours), но не позже начала,
            // если начало было ещё впереди. Уже просроченные фоновая задача переведёт в «Истекла» при первом проходе.
            migrationBuilder.Sql("""
                UPDATE bookings.bookings b
                SET hold_until = CASE
                    WHEN b.starts_at > b.created_at AND b.starts_at < b.created_at + x.hold THEN b.starts_at
                    ELSE b.created_at + x.hold
                END
                FROM (
                    SELECT b2.id,
                           CASE WHEN b2.kind = 'Hourly'
                                THEN make_interval(mins => COALESCE((s.hourly_policy ->> 'PendingHoldMinutes')::int, 30))
                                ELSE make_interval(hours => s.pending_hold_hours)
                           END AS hold
                    FROM bookings.bookings b2
                    JOIN hotel.hotel_settings s ON s.hotel_id = b2.hotel_id
                    WHERE b2.status IN ('Pending', 'AwaitingPayment') AND b2.hold_until IS NULL
                ) x
                WHERE b.id = x.id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bookings_hold_until",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bookings_penalty",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "auto_no_show_enabled",
                schema: "hotel",
                table: "hotel_settings");

            migrationBuilder.DropColumn(
                name: "hold_until",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "penalty_amount",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "penalty_kind",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "penalty_waived",
                schema: "bookings",
                table: "bookings");
        }
    }
}
