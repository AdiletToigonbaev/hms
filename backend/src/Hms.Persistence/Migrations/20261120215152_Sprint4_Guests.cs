using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sprint4_Guests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "guests");

            migrationBuilder.CreateTable(
                name: "guests",
                schema: "guests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hotel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    whats_app_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    anonymized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "guest_consents",
                schema: "guests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    guest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    granted = table.Column<bool>(type: "boolean", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guest_consents", x => x.id);
                    table.ForeignKey(
                        name: "fk_guest_consents_guests_guest_id",
                        column: x => x.guest_id,
                        principalSchema: "guests",
                        principalTable: "guests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_guest_consents_guest_id_type_recorded_at",
                schema: "guests",
                table: "guest_consents",
                columns: new[] { "guest_id", "type", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_guests_hotel_id_created_at",
                schema: "guests",
                table: "guests",
                columns: new[] { "hotel_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_guests_hotel_id_email",
                schema: "guests",
                table: "guests",
                columns: new[] { "hotel_id", "email" });

            migrationBuilder.CreateIndex(
                name: "ix_guests_hotel_id_phone",
                schema: "guests",
                table: "guests",
                columns: new[] { "hotel_id", "phone" });

            migrationBuilder.CreateIndex(
                name: "ix_guests_user_id",
                schema: "guests",
                table: "guests",
                column: "user_id",
                unique: true,
                filter: "user_id IS NOT NULL");

            // Перенос существующих данных в базу гостей (Sprint 4):
            // 1) аккаунты гостей с сайта → карточки; согласия при регистрации (записаны в аудит в Sprint 1) → журнал согласий;
            // 2) контакты из броней → привязка к найденной карточке (телефон, затем email) или новая карточка.
            // Гостиница одна; если её ещё нет (пустая БД до сидеров) — переносить нечего.
            migrationBuilder.Sql("""
                INSERT INTO guests.guests (id, hotel_id, user_id, full_name, phone, email, source, created_at, updated_at)
                SELECT gen_random_uuid(), h.id, u.id, u.full_name, u.phone_number, lower(u.email), 'Portal', u.created_at, u.created_at
                FROM identity.users u
                CROSS JOIN (SELECT id FROM hotel.hotels LIMIT 1) h
                WHERE u.user_type = 'Guest';

                INSERT INTO guests.guest_consents (id, guest_id, type, granted, source, recorded_by, recorded_at)
                SELECT gen_random_uuid(), g.id, c.type, true, 'Portal', g.user_id, (a.after -> 'consents' ->> 'at')::timestamptz
                FROM audit.audit_logs a
                JOIN guests.guests g ON g.user_id::text = a.entity_id
                CROSS JOIN LATERAL (VALUES ('PersonalData', 'personalData'), ('WhatsApp', 'whatsApp')) AS c(type, json_key)
                WHERE a.action = 'auth.registered'
                  AND (a.after -> 'consents' ->> c.json_key)::boolean;

                UPDATE bookings.bookings b SET guest_id = g.id
                FROM guests.guests g
                WHERE b.guest_id IS NULL AND g.hotel_id = b.hotel_id
                  AND ((b.guest_phone IS NOT NULL AND g.phone = b.guest_phone)
                    OR (b.guest_phone IS NULL AND b.guest_email IS NOT NULL AND g.email = lower(b.guest_email)));

                WITH src AS (
                    SELECT b.id AS booking_id, b.hotel_id, b.guest_name, b.guest_phone, lower(b.guest_email) AS guest_email, b.created_at,
                           coalesce(b.guest_phone, lower(b.guest_email), 'name:' || lower(b.guest_name)) AS k
                    FROM bookings.bookings b
                    WHERE b.guest_id IS NULL),
                first_seen AS (
                    SELECT DISTINCT ON (hotel_id, k) hotel_id, k, guest_name, guest_phone, guest_email, created_at
                    FROM src ORDER BY hotel_id, k, created_at),
                created AS (
                    INSERT INTO guests.guests (id, hotel_id, full_name, phone, email, source, created_at, updated_at)
                    SELECT gen_random_uuid(), hotel_id, guest_name, guest_phone, guest_email, 'Booking', created_at, created_at
                    FROM first_seen
                    RETURNING id, hotel_id, coalesce(phone, email, 'name:' || lower(full_name)) AS k)
                UPDATE bookings.bookings b SET guest_id = created.id
                FROM src JOIN created ON created.hotel_id = src.hotel_id AND created.k = src.k
                WHERE b.id = src.booking_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE bookings.bookings SET guest_id = NULL;");

            migrationBuilder.DropTable(
                name: "guest_consents",
                schema: "guests");

            migrationBuilder.DropTable(
                name: "guests",
                schema: "guests");
        }
    }
}
