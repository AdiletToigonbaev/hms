using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sprint6_CleaningPerCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "cleaning_hourly_minutes",
                schema: "rooms",
                table: "room_types",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "cleaning_nightly_minutes",
                schema: "rooms",
                table: "room_types",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "cleaning_buffer_nightly_minutes",
                schema: "hotel",
                table: "hotel_settings",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            // Для существующей гостиницы ничего не меняется: уборка после посуточной брони = прежнее общее время уборки.
            migrationBuilder.Sql("UPDATE hotel.hotel_settings SET cleaning_buffer_nightly_minutes = cleaning_buffer_minutes;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cleaning_hourly_minutes",
                schema: "rooms",
                table: "room_types");

            migrationBuilder.DropColumn(
                name: "cleaning_nightly_minutes",
                schema: "rooms",
                table: "room_types");

            migrationBuilder.DropColumn(
                name: "cleaning_buffer_nightly_minutes",
                schema: "hotel",
                table: "hotel_settings");
        }
    }
}
