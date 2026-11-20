using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sprint3_HourlyBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bookings_hotel_id_status_check_in",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bookings_dates",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.AddColumn<decimal>(
                name: "hourly_price_override",
                schema: "rooms",
                table: "rooms",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "hourly_price",
                schema: "rooms",
                table: "room_types",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "unit",
                schema: "pricing",
                table: "room_rates",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Night");

            migrationBuilder.AddColumn<int>(
                name: "cleaning_buffer_minutes",
                schema: "hotel",
                table: "hotel_settings",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "hourly_max_hours",
                schema: "hotel",
                table: "hotel_settings",
                type: "integer",
                nullable: false,
                defaultValue: 12);

            migrationBuilder.AddColumn<int>(
                name: "hourly_min_hours",
                schema: "hotel",
                table: "hotel_settings",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ends_at",
                schema: "bookings",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<decimal>(
                name: "extensions_amount",
                schema: "bookings",
                table: "bookings",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "hours",
                schema: "bookings",
                table: "bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "bookings",
                table: "bookings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Nightly");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "starts_at",
                schema: "bookings",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "blocked_until",
                schema: "bookings",
                table: "booking_rooms",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ends_at",
                schema: "bookings",
                table: "booking_rooms",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "starts_at",
                schema: "bookings",
                table: "booking_rooms",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Почасовые брони (решение заказчика 2026-11-20): занятость номера — по времени, а не по датам.
            // Существующие посуточные брони получают время заезда/выезда из настроек гостиницы;
            // уборка после выезда — не дольше промежутка между выездом и заездом.
            migrationBuilder.Sql("""
                UPDATE bookings.booking_rooms br SET
                    starts_at = (br.check_in + hs.check_in_time) AT TIME ZONE h.time_zone,
                    ends_at = (br.check_out + hs.check_out_time) AT TIME ZONE h.time_zone,
                    blocked_until = ((br.check_out + hs.check_out_time) AT TIME ZONE h.time_zone)
                        + LEAST(make_interval(mins => hs.cleaning_buffer_minutes), GREATEST(interval '0', hs.check_in_time - hs.check_out_time))
                FROM bookings.bookings b
                JOIN hotel.hotels h ON h.id = b.hotel_id
                JOIN hotel.hotel_settings hs ON hs.hotel_id = h.id
                WHERE b.id = br.booking_id;

                UPDATE bookings.bookings b SET starts_at = br.starts_at, ends_at = br.ends_at
                FROM bookings.booking_rooms br WHERE br.booking_id = b.id;

                ALTER TABLE bookings.booking_rooms DROP CONSTRAINT ex_booking_rooms_no_overlap;
                ALTER TABLE bookings.booking_rooms DROP CONSTRAINT ck_booking_rooms_dates;
                ALTER TABLE bookings.booking_rooms DROP COLUMN stay_range;

                ALTER TABLE bookings.booking_rooms
                    ADD CONSTRAINT ck_booking_rooms_period
                    CHECK (check_out >= check_in AND ends_at > starts_at AND blocked_until >= ends_at);

                -- Защита от двойного бронирования по времени: [начало, конец + уборка).
                ALTER TABLE bookings.booking_rooms
                    ADD COLUMN occupancy tstzrange GENERATED ALWAYS AS (tstzrange(starts_at, blocked_until, '[)')) STORED;

                ALTER TABLE bookings.booking_rooms
                    ADD CONSTRAINT ex_booking_rooms_no_overlap
                    EXCLUDE USING gist (room_id WITH =, occupancy WITH &&) WHERE (is_blocking);
                """);

            migrationBuilder.CreateTable(
                name: "booking_extensions",
                schema: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    previous_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    new_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    price_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_extensions", x => x.id);
                    table.CheckConstraint("ck_booking_extensions_qty", "quantity > 0 AND amount >= 0 AND new_ends_at > previous_ends_at");
                    table.ForeignKey(
                        name: "fk_booking_extensions_bookings_booking_id",
                        column: x => x.booking_id,
                        principalSchema: "bookings",
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_hotel_id_status_starts_at",
                schema: "bookings",
                table: "bookings",
                columns: new[] { "hotel_id", "status", "starts_at" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_bookings_dates",
                schema: "bookings",
                table: "bookings",
                sql: "check_out >= check_in AND ends_at > starts_at");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bookings_hours",
                schema: "bookings",
                table: "bookings",
                sql: "hours IS NULL OR hours BETWEEN 1 AND 24");

            migrationBuilder.CreateIndex(
                name: "ix_booking_rooms_room_id_starts_at",
                schema: "bookings",
                table: "booking_rooms",
                columns: new[] { "room_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_booking_extensions_booking_id",
                schema: "bookings",
                table: "booking_extensions",
                column: "booking_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Откат к датам: почасовые брони внутри одного дня откат не переживут (check_out > check_in) — удалите их до отката.
            migrationBuilder.Sql("""
                ALTER TABLE bookings.booking_rooms DROP CONSTRAINT IF EXISTS ex_booking_rooms_no_overlap;
                ALTER TABLE bookings.booking_rooms DROP CONSTRAINT IF EXISTS ck_booking_rooms_period;
                ALTER TABLE bookings.booking_rooms DROP COLUMN IF EXISTS occupancy;
                ALTER TABLE bookings.booking_rooms ADD CONSTRAINT ck_booking_rooms_dates CHECK (check_out > check_in);
                ALTER TABLE bookings.booking_rooms
                    ADD COLUMN stay_range daterange GENERATED ALWAYS AS (daterange(check_in, check_out, '[)')) STORED;
                ALTER TABLE bookings.booking_rooms
                    ADD CONSTRAINT ex_booking_rooms_no_overlap
                    EXCLUDE USING gist (room_id WITH =, stay_range WITH &&) WHERE (is_blocking);
                """);

            migrationBuilder.DropTable(
                name: "booking_extensions",
                schema: "bookings");

            migrationBuilder.DropIndex(
                name: "ix_bookings_hotel_id_status_starts_at",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bookings_dates",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bookings_hours",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "ix_booking_rooms_room_id_starts_at",
                schema: "bookings",
                table: "booking_rooms");

            migrationBuilder.DropColumn(
                name: "hourly_price_override",
                schema: "rooms",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "hourly_price",
                schema: "rooms",
                table: "room_types");

            migrationBuilder.DropColumn(
                name: "unit",
                schema: "pricing",
                table: "room_rates");

            migrationBuilder.DropColumn(
                name: "cleaning_buffer_minutes",
                schema: "hotel",
                table: "hotel_settings");

            migrationBuilder.DropColumn(
                name: "hourly_max_hours",
                schema: "hotel",
                table: "hotel_settings");

            migrationBuilder.DropColumn(
                name: "hourly_min_hours",
                schema: "hotel",
                table: "hotel_settings");

            migrationBuilder.DropColumn(
                name: "ends_at",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "extensions_amount",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "hours",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "starts_at",
                schema: "bookings",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "blocked_until",
                schema: "bookings",
                table: "booking_rooms");

            migrationBuilder.DropColumn(
                name: "ends_at",
                schema: "bookings",
                table: "booking_rooms");

            migrationBuilder.DropColumn(
                name: "starts_at",
                schema: "bookings",
                table: "booking_rooms");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_hotel_id_status_check_in",
                schema: "bookings",
                table: "bookings",
                columns: new[] { "hotel_id", "status", "check_in" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_bookings_dates",
                schema: "bookings",
                table: "bookings",
                sql: "check_out > check_in");
        }
    }
}
