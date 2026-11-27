using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HotelGuestLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "two_gis_url",
                schema: "hotel",
                table: "hotels",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "whats_app_phone",
                schema: "hotel",
                table: "hotels",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "two_gis_url",
                schema: "hotel",
                table: "hotels");

            migrationBuilder.DropColumn(
                name: "whats_app_phone",
                schema: "hotel",
                table: "hotels");
        }
    }
}
