using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Ledger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ledger");

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "ledger",
                columns: table => new
                {
                    number = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    parent_number = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.number);
                    table.ForeignKey(
                        name: "FK_accounts_accounts_parent_number",
                        column: x => x.parent_number,
                        principalSchema: "ledger",
                        principalTable: "accounts",
                        principalColumn: "number",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                schema: "ledger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    external_ref = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    sepa_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    counterparty_iban = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    counterparty_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    review_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reviewed_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "journal_entries",
                schema: "ledger",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_number = table.Column<int>(type: "integer", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    direction = table.Column<short>(type: "smallint", nullable: false),
                    posted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_entries", x => x.id);
                    table.CheckConstraint("ck_journal_entries_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_journal_entries_direction", "direction IN (-1, 1)");
                    table.ForeignKey(
                        name: "FK_journal_entries_accounts_account_number",
                        column: x => x.account_number,
                        principalSchema: "ledger",
                        principalTable: "accounts",
                        principalColumn: "number",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_journal_entries_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalSchema: "ledger",
                        principalTable: "transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_code",
                schema: "ledger",
                table: "accounts",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounts_parent_number",
                schema: "ledger",
                table: "accounts",
                column: "parent_number");

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_account_number_posted_at",
                schema: "ledger",
                table: "journal_entries",
                columns: new[] { "account_number", "posted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_customer_id",
                schema: "ledger",
                table: "journal_entries",
                column: "customer_id",
                filter: "customer_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_transaction_id",
                schema: "ledger",
                table: "journal_entries",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_customer_id",
                schema: "ledger",
                table: "transactions",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_external_ref",
                schema: "ledger",
                table: "transactions",
                column: "external_ref");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_status",
                schema: "ledger",
                table: "transactions",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "journal_entries",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "transactions",
                schema: "ledger");
        }
    }
}
