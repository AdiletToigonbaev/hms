using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sprint6_BookingNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bookings_hotel_id_number",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropSequence(
                name: "booking_number_seq",
                schema: "bookings");

            migrationBuilder.AddColumn<int>(
                name: "number_year",
                schema: "bookings",
                table: "bookings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Брони до перехода на «Ч-2111-07» сохраняют свои номера «26-000042»; год — год создания по времени гостиницы.
            migrationBuilder.Sql("""
                UPDATE bookings.bookings b
                SET number_year = EXTRACT(YEAR FROM b.created_at AT TIME ZONE COALESCE(
                    (SELECT h.time_zone FROM hotel.hotels h WHERE h.id = b.hotel_id), 'Asia/Bishkek'))::int;
                """);

            migrationBuilder.CreateTable(
                name: "booking_number_counters",
                schema: "bookings",
                columns: table => new
                {
                    hotel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    last = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_number_counters", x => new { x.hotel_id, x.day });
                });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_hotel_id_number_year_number",
                schema: "bookings",
                table: "bookings",
                columns: new[] { "hotel_id", "number_year", "number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "booking_number_counters",
                schema: "bookings");

            migrationBuilder.DropIndex(
                name: "ix_bookings_hotel_id_number_year_number",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "number_year",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.CreateSequence(
                name: "booking_number_seq",
                schema: "bookings");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_hotel_id_number",
                schema: "bookings",
                table: "bookings",
                columns: new[] { "hotel_id", "number" },
                unique: true);
        }
    }
}
