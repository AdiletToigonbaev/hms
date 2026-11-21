using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sprint6_Finance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "finance");

            migrationBuilder.CreateTable(
                name: "expense_categories",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hotel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hotel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guest_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    channel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reversal_of_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    refunded_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    registered_by = table.Column<Guid>(type: "uuid", nullable: true),
                    comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amount", "(type = 'Reversal' AND amount < 0) OR (type <> 'Reversal' AND amount > 0)");
                    table.CheckConstraint("ck_payments_refunded", "refunded_amount >= 0 AND refunded_amount <= abs(amount)");
                });

            migrationBuilder.CreateTable(
                name: "expenses",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hotel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    supplier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_voided = table.Column<bool>(type: "boolean", nullable: false),
                    voided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expenses", x => x.id);
                    table.CheckConstraint("ck_expenses_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_expenses_expense_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "finance",
                        principalTable: "expense_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_transactions",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    provider_txn_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_transactions_payments_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "finance",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refunds",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hotel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    paid_by = table.Column<Guid>(type: "uuid", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refunds", x => x.id);
                    table.CheckConstraint("ck_refunds_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_refunds_payments_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "finance",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_expense_categories_hotel_id_code",
                schema: "finance",
                table: "expense_categories",
                columns: new[] { "hotel_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expenses_category_id",
                schema: "finance",
                table: "expenses",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_created_by",
                schema: "finance",
                table: "expenses",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_hotel_id_date",
                schema: "finance",
                table: "expenses",
                columns: new[] { "hotel_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_payment_id_occurred_at",
                schema: "finance",
                table: "payment_transactions",
                columns: new[] { "payment_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_provider_provider_txn_id_kind",
                schema: "finance",
                table: "payment_transactions",
                columns: new[] { "provider", "provider_txn_id", "kind" },
                unique: true,
                filter: "provider_txn_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payments_booking_id",
                schema: "finance",
                table: "payments",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_guest_id_paid_at",
                schema: "finance",
                table: "payments",
                columns: new[] { "guest_id", "paid_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_hotel_id_paid_at",
                schema: "finance",
                table: "payments",
                columns: new[] { "hotel_id", "paid_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_reversal_of_payment_id",
                schema: "finance",
                table: "payments",
                column: "reversal_of_payment_id",
                unique: true,
                filter: "reversal_of_payment_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_booking_id",
                schema: "finance",
                table: "refunds",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_hotel_id_status_requested_at",
                schema: "finance",
                table: "refunds",
                columns: new[] { "hotel_id", "status", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_refunds_payment_id",
                schema: "finance",
                table: "refunds",
                column: "payment_id");

            // Платежи неизменяемы (ТЗ: история финансовых операций; исправления — сторно): у платежа меняются только
            // статус, выплаченная сумма возвратов и версия; удалять нельзя. Журнал операций — только вставка.
            // TRUNCATE не блокируется: приложение его не выполняет, он нужен только для сброса локальной базы разработки.
            migrationBuilder.Sql("""
                CREATE FUNCTION finance.prevent_payment_change() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'finance.payments: DELETE is not allowed' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF (NEW.id, NEW.hotel_id, NEW.booking_id, NEW.guest_id, NEW.type, NEW.method, NEW.channel, NEW.amount,
                        NEW.reversal_of_payment_id, NEW.paid_at, NEW.registered_by, NEW.comment, NEW.created_at)
                       IS DISTINCT FROM
                       (OLD.id, OLD.hotel_id, OLD.booking_id, OLD.guest_id, OLD.type, OLD.method, OLD.channel, OLD.amount,
                        OLD.reversal_of_payment_id, OLD.paid_at, OLD.registered_by, OLD.comment, OLD.created_at) THEN
                        RAISE EXCEPTION 'finance.payments: only status and refunded amount can change' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER payments_immutable
                    BEFORE UPDATE OR DELETE ON finance.payments
                    FOR EACH ROW EXECUTE FUNCTION finance.prevent_payment_change();

                CREATE FUNCTION finance.prevent_transaction_change() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'finance.payment_transactions is append-only (% is not allowed)', TG_OP USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER payment_transactions_append_only
                    BEFORE UPDATE OR DELETE ON finance.payment_transactions
                    FOR EACH ROW EXECUTE FUNCTION finance.prevent_transaction_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS payment_transactions_append_only ON finance.payment_transactions;
                DROP FUNCTION IF EXISTS finance.prevent_transaction_change();
                DROP TRIGGER IF EXISTS payments_immutable ON finance.payments;
                DROP FUNCTION IF EXISTS finance.prevent_payment_change();
                """);

            migrationBuilder.DropTable(
                name: "expenses",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payment_transactions",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "refunds",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "expense_categories",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "finance");
        }
    }
}
