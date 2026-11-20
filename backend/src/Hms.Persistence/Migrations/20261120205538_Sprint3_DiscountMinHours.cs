using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sprint3_DiscountMinHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "min_hours",
                schema: "pricing",
                table: "discounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "ck_discounts_min_hours",
                schema: "pricing",
                table: "discounts",
                sql: "min_hours BETWEEN 0 AND 24");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_discounts_min_hours",
                schema: "pricing",
                table: "discounts");

            migrationBuilder.DropColumn(
                name: "min_hours",
                schema: "pricing",
                table: "discounts");
        }
    }
}
