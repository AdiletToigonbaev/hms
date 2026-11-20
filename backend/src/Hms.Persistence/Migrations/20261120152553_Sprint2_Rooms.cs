using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sprint2_Rooms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "rooms");

            migrationBuilder.CreateTable(
                name: "amenities",
                schema: "rooms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hotel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_amenities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "room_types",
                schema: "rooms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hotel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    beds_count = table.Column<int>(type: "integer", nullable: false),
                    bed_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    base_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room_types", x => x.id);
                    table.CheckConstraint("ck_room_types_base_price", "base_price > 0");
                    table.CheckConstraint("ck_room_types_capacity", "capacity BETWEEN 1 AND 20");
                });

            migrationBuilder.CreateTable(
                name: "room_type_amenities",
                schema: "rooms",
                columns: table => new
                {
                    room_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amenity_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room_type_amenities", x => new { x.room_type_id, x.amenity_id });
                    table.ForeignKey(
                        name: "fk_room_type_amenities_amenities_amenity_id",
                        column: x => x.amenity_id,
                        principalSchema: "rooms",
                        principalTable: "amenities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_room_type_amenities_room_types_room_type_id",
                        column: x => x.room_type_id,
                        principalSchema: "rooms",
                        principalTable: "room_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rooms",
                schema: "rooms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hotel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    floor = table.Column<int>(type: "integer", nullable: true),
                    capacity = table.Column<int>(type: "integer", nullable: true),
                    beds_count = table.Column<int>(type: "integer", nullable: true),
                    bed_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    price_override = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rooms", x => x.id);
                    table.CheckConstraint("ck_rooms_capacity", "capacity IS NULL OR capacity BETWEEN 1 AND 20");
                    table.CheckConstraint("ck_rooms_price_override", "price_override IS NULL OR price_override > 0");
                    table.ForeignKey(
                        name: "fk_rooms_room_types_room_type_id",
                        column: x => x.room_type_id,
                        principalSchema: "rooms",
                        principalTable: "room_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "room_photos",
                schema: "rooms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    room_id = table.Column<Guid>(type: "uuid", nullable: true),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room_photos", x => x.id);
                    table.CheckConstraint("ck_room_photos_owner", "(room_type_id IS NULL) <> (room_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_room_photos_room_types_room_type_id",
                        column: x => x.room_type_id,
                        principalSchema: "rooms",
                        principalTable: "room_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_room_photos_rooms_room_id",
                        column: x => x.room_id,
                        principalSchema: "rooms",
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_room_photos_stored_files_file_id",
                        column: x => x.file_id,
                        principalSchema: "platform",
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "room_status_history",
                schema: "rooms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    to_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room_status_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_room_status_history_rooms_room_id",
                        column: x => x.room_id,
                        principalSchema: "rooms",
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_amenities_hotel_id_code",
                schema: "rooms",
                table: "amenities",
                columns: new[] { "hotel_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_room_photos_file_id",
                schema: "rooms",
                table: "room_photos",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_room_photos_room_id_sort_order",
                schema: "rooms",
                table: "room_photos",
                columns: new[] { "room_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_room_photos_room_type_id_sort_order",
                schema: "rooms",
                table: "room_photos",
                columns: new[] { "room_type_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_room_status_history_room_id_changed_at",
                schema: "rooms",
                table: "room_status_history",
                columns: new[] { "room_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_room_type_amenities_amenity_id",
                schema: "rooms",
                table: "room_type_amenities",
                column: "amenity_id");

            migrationBuilder.CreateIndex(
                name: "ix_room_types_hotel_id_is_archived_sort_order",
                schema: "rooms",
                table: "room_types",
                columns: new[] { "hotel_id", "is_archived", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_rooms_hotel_id_number",
                schema: "rooms",
                table: "rooms",
                columns: new[] { "hotel_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rooms_room_type_id_status",
                schema: "rooms",
                table: "rooms",
                columns: new[] { "room_type_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "room_photos",
                schema: "rooms");

            migrationBuilder.DropTable(
                name: "room_status_history",
                schema: "rooms");

            migrationBuilder.DropTable(
                name: "room_type_amenities",
                schema: "rooms");

            migrationBuilder.DropTable(
                name: "rooms",
                schema: "rooms");

            migrationBuilder.DropTable(
                name: "amenities",
                schema: "rooms");

            migrationBuilder.DropTable(
                name: "room_types",
                schema: "rooms");
        }
    }
}
