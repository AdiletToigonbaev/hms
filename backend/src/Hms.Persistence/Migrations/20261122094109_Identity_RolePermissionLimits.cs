using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Identity_RolePermissionLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "limit_amount",
                schema: "identity",
                table: "role_permissions",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "limit_amount",
                schema: "identity",
                table: "role_permissions");
        }
    }
}
