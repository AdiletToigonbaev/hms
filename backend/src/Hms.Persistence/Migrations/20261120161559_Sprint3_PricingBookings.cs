using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sprint3_PricingBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "bookings");

            migrationBuilder.EnsureSchema(
                name: "pricing");

            migrationBuilder.CreateSequence(
                name: "booking_number_seq",
                schema: "bookings");

            migrationBuilder.CreateTable(
                name: "bookings",
                schema: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hotel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    check_in = table.Column<DateOnly>(type: "date", nullable: false),
                    check_out = table.Column<DateOnly>(type: "date", nullable: false),
                    adults = table.Column<int>(type: "integer", nullable: false),
                    children = table.Column<int>(type: "integer", nullable: false),
                    guest_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    guest_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    guest_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    guest_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    discount_id = table.Column<Guid>(type: "uuid", nullable: true),
                    promo_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    accommodation_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    services_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    price_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookings", x => x.id);
                    table.CheckConstraint("ck_bookings_adults", "adults >= 1");
                    table.CheckConstraint("ck_bookings_amounts", "total_amount >= 0 AND discount_amount >= 0 AND discount_amount <= accommodation_amount");
                    table.CheckConstraint("ck_bookings_dates", "check_out > check_in");
                });

            migrationBuilder.CreateTable(
                name: "discounts",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hotel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    value = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    min_nights = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discounts", x => x.id);
                    table.CheckConstraint("ck_discounts_value", "value > 0");
                });

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                schema: "platform",
                columns: table => new
                {
                    scope = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    response_status = table.Column<int>(type: "integer", nullable: true),
                    response_body = table.Column<string>(type: "jsonb", nullable: true),
                    is_problem = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_keys", x => new { x.scope, x.key });
                });

            migrationBuilder.CreateTable(
                name: "room_rates",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hotel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    date_from = table.Column<DateOnly>(type: "date", nullable: false),
                    date_to = table.Column<DateOnly>(type: "date", nullable: false),
                    days_of_week = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room_rates", x => x.id);
                    table.CheckConstraint("ck_room_rates_days", "days_of_week BETWEEN 1 AND 127");
                    table.CheckConstraint("ck_room_rates_period", "date_to >= date_from");
                    table.CheckConstraint("ck_room_rates_price", "price > 0");
                });

            migrationBuilder.CreateTable(
                name: "services",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hotel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    is_per_night = table.Column<bool>(type: "boolean", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_services", x => x.id);
                    table.CheckConstraint("ck_services_price", "price > 0");
                });

            migrationBuilder.CreateTable(
                name: "booking_history",
                schema: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    old_values = table.Column<string>(type: "jsonb", nullable: true),
                    new_values = table.Column<string>(type: "jsonb", nullable: true),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_booking_history_bookings_booking_id",
                        column: x => x.booking_id,
                        principalSchema: "bookings",
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "booking_rooms",
                schema: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    check_in = table.Column<DateOnly>(type: "date", nullable: false),
                    check_out = table.Column<DateOnly>(type: "date", nullable: false),
                    is_blocking = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_rooms", x => x.id);
                    table.ForeignKey(
                        name: "fk_booking_rooms_bookings_booking_id",
                        column: x => x.booking_id,
                        principalSchema: "bookings",
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "booking_services",
                schema: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    per_night = table.Column<bool>(type: "boolean", nullable: false),
                    total = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    added_by = table.Column<Guid>(type: "uuid", nullable: true),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_services", x => x.id);
                    table.CheckConstraint("ck_booking_services_qty", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_booking_services_bookings_booking_id",
                        column: x => x.booking_id,
                        principalSchema: "bookings",
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_booking_history_booking_id_changed_at",
                schema: "bookings",
                table: "booking_history",
                columns: new[] { "booking_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_booking_rooms_booking_id",
                schema: "bookings",
                table: "booking_rooms",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_booking_rooms_room_id_check_in",
                schema: "bookings",
                table: "booking_rooms",
                columns: new[] { "room_id", "check_in" });

            migrationBuilder.CreateIndex(
                name: "ix_booking_services_booking_id",
                schema: "bookings",
                table: "booking_services",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_guest_id",
                schema: "bookings",
                table: "bookings",
                column: "guest_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_hotel_id_number",
                schema: "bookings",
                table: "bookings",
                columns: new[] { "hotel_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bookings_hotel_id_status_check_in",
                schema: "bookings",
                table: "bookings",
                columns: new[] { "hotel_id", "status", "check_in" });

            migrationBuilder.CreateIndex(
                name: "ix_discounts_hotel_id_code",
                schema: "pricing",
                table: "discounts",
                columns: new[] { "hotel_id", "code" },
                unique: true,
                filter: "code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_expires_at",
                schema: "platform",
                table: "idempotency_keys",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_room_rates_room_type_id_date_from_date_to",
                schema: "pricing",
                table: "room_rates",
                columns: new[] { "room_type_id", "date_from", "date_to" });

            migrationBuilder.CreateIndex(
                name: "ix_services_hotel_id_is_active",
                schema: "pricing",
                table: "services",
                columns: new[] { "hotel_id", "is_active" });

            // Защита от двойного бронирования (ТЗ §5, BOOK-3): номер не может иметь пересекающиеся
            // действующие брони. Интервал [check_in, check_out) — выезд в день заезда следующего гостя допустим.
            // Ограничение работает в БД, поэтому выдерживает параллельные запросы, а не только проверку в коде.
            migrationBuilder.Sql("""
                ALTER TABLE bookings.booking_rooms
                    ADD COLUMN stay_range daterange GENERATED ALWAYS AS (daterange(check_in, check_out, '[)')) STORED;

                ALTER TABLE bookings.booking_rooms
                    ADD CONSTRAINT ck_booking_rooms_dates CHECK (check_out > check_in);

                ALTER TABLE bookings.booking_rooms
                    ADD CONSTRAINT ex_booking_rooms_no_overlap
                    EXCLUDE USING gist (room_id WITH =, stay_range WITH &&) WHERE (is_blocking);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE bookings.booking_rooms DROP CONSTRAINT IF EXISTS ex_booking_rooms_no_overlap;
                ALTER TABLE bookings.booking_rooms DROP CONSTRAINT IF EXISTS ck_booking_rooms_dates;
                ALTER TABLE bookings.booking_rooms DROP COLUMN IF EXISTS stay_range;
                """);

            migrationBuilder.DropTable(
                name: "booking_history",
                schema: "bookings");

            migrationBuilder.DropTable(
                name: "booking_rooms",
                schema: "bookings");

            migrationBuilder.DropTable(
                name: "booking_services",
                schema: "bookings");

            migrationBuilder.DropTable(
                name: "discounts",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "idempotency_keys",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "room_rates",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "services",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "bookings",
                schema: "bookings");

            migrationBuilder.DropSequence(
                name: "booking_number_seq",
                schema: "bookings");
        }
    }
}
