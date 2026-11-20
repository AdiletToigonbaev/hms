using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sprint3_HourlyPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "hourly_policy",
                schema: "hotel",
                table: "hotel_settings",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            // Правила почасовых броней (решение заказчика 2026-11-21, roadmap §6.1a) — значения по умолчанию для существующей гостиницы.
            migrationBuilder.Sql("""
                UPDATE hotel.hotel_settings SET hourly_policy = '{"FreeCancelMinutesBeforeStart": 120, "LateCancelPenaltyHours": 1, "NoShowAfterMinutes": 30, "NoShowPenaltyHours": 1, "PendingHoldMinutes": 30, "RefundUnusedOnEarlyDeparture": false}'::jsonb;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "hourly_policy",
                schema: "hotel",
                table: "hotel_settings");
        }
    }
}
