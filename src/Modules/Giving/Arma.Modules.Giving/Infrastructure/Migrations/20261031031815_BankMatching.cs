using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arma.Modules.Giving.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BankMatching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bank_statements",
                schema: "giving",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    credit_count = table.Column<int>(type: "integer", nullable: false),
                    duplicate_count = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_statements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bank_lines",
                schema: "giving",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    statement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    booked_on = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    counterparty = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    bank_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    suggested_contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    suggested_fund_id = table.Column<Guid>(type: "uuid", nullable: true),
                    suggestion_reason = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    donation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_lines_bank_statements_statement_id",
                        column: x => x.statement_id,
                        principalSchema: "giving",
                        principalTable: "bank_statements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_lines_statement_id",
                schema: "giving",
                table: "bank_lines",
                column: "statement_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_lines_tenant_id_contact_id",
                schema: "giving",
                table: "bank_lines",
                columns: new[] { "tenant_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_lines_tenant_id_iban",
                schema: "giving",
                table: "bank_lines",
                columns: new[] { "tenant_id", "iban" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_lines_tenant_id_key",
                schema: "giving",
                table: "bank_lines",
                columns: new[] { "tenant_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bank_lines_tenant_id_status_booked_on",
                schema: "giving",
                table: "bank_lines",
                columns: new[] { "tenant_id", "status", "booked_on" });

            migrationBuilder.Sql("""
                ALTER TABLE giving.bank_statements ENABLE ROW LEVEL SECURITY;
                ALTER TABLE giving.bank_statements FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON giving.bank_statements
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE giving.bank_lines ENABLE ROW LEVEL SECURITY;
                ALTER TABLE giving.bank_lines FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON giving.bank_lines
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_lines",
                schema: "giving");

            migrationBuilder.DropTable(
                name: "bank_statements",
                schema: "giving");
        }
    }
}
