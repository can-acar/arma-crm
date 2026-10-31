using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arma.Modules.Invoicing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialInvoicing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "invoicing");

            migrationBuilder.CreateTable(
                name: "invoice_settings",
                schema: "invoicing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    prefix = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    seller_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    seller_tax_id = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    seller_tax_office = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    seller_street = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    seller_district = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    seller_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    seller_postal_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    seller_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    seller_phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    seller_website = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    from_quotes = table.Column<bool>(type: "boolean", nullable: false),
                    from_dues = table.Column<bool>(type: "boolean", nullable: false),
                    auto_send = table.Column<bool>(type: "boolean", nullable: false),
                    dues_vat_rate = table.Column<int>(type: "integer", nullable: false),
                    exemption_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    exemption_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                schema: "invoicing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    uuid = table.Column<Guid>(type: "uuid", nullable: false),
                    profile = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    buyer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    buyer_first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    buyer_last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    buyer_tax_id = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    buyer_tax_office = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    buyer_street = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    buyer_district = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    buyer_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    buyer_postal_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    buyer_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    buyer_phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    provider_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    transmitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                schema: "invoicing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    vat_rate = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_invoice_lines_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "invoicing",
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_invoice_id",
                schema: "invoicing",
                table: "invoice_lines",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_settings_tenant_id",
                schema: "invoicing",
                table: "invoice_settings",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_tenant_id_contact_id",
                schema: "invoicing",
                table: "invoices",
                columns: new[] { "tenant_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_tenant_id_number",
                schema: "invoicing",
                table: "invoices",
                columns: new[] { "tenant_id", "number" },
                unique: true,
                filter: "number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_tenant_id_source_kind_source_id",
                schema: "invoicing",
                table: "invoices",
                columns: new[] { "tenant_id", "source_kind", "source_id" },
                unique: true,
                filter: "source_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_tenant_id_status_issued_on",
                schema: "invoicing",
                table: "invoices",
                columns: new[] { "tenant_id", "status", "issued_on" });

            migrationBuilder.Sql("""
                CREATE TABLE invoicing.invoice_sequences (
                    tenant_id uuid NOT NULL,
                    prefix varchar(3) NOT NULL,
                    year integer NOT NULL,
                    last_number bigint NOT NULL,
                    PRIMARY KEY (tenant_id, prefix, year)
                );
                ALTER TABLE invoicing.invoices ENABLE ROW LEVEL SECURITY;
                ALTER TABLE invoicing.invoices FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON invoicing.invoices
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE invoicing.invoice_lines ENABLE ROW LEVEL SECURITY;
                ALTER TABLE invoicing.invoice_lines FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON invoicing.invoice_lines
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE invoicing.invoice_settings ENABLE ROW LEVEL SECURITY;
                ALTER TABLE invoicing.invoice_settings FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON invoicing.invoice_settings
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE invoicing.invoice_sequences ENABLE ROW LEVEL SECURITY;
                ALTER TABLE invoicing.invoice_sequences FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON invoicing.invoice_sequences
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE invoicing.invoice_sequences;");
            migrationBuilder.DropTable(
                name: "invoice_lines",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "invoice_settings",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "invoices",
                schema: "invoicing");
        }
    }
}
