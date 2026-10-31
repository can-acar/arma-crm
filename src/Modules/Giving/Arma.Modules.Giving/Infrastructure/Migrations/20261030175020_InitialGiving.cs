using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arma.Modules.Giving.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialGiving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "giving");

            migrationBuilder.CreateTable(
                name: "funds",
                schema: "giving",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false, collation: "tr-TR-x-icu"),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_funds", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "receipt_settings",
                schema: "giving",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    legal_info = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    footer = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    number_prefix = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipt_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "donations",
                schema: "giving",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    fund_id = table.Column<Guid>(type: "uuid", nullable: true),
                    method = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    in_kind_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    receipt_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    receipt_issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    receipt_void_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    provider_payment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_donations", x => x.id);
                    table.ForeignKey(
                        name: "fk_donations_funds_fund_id",
                        column: x => x.fund_id,
                        principalSchema: "giving",
                        principalTable: "funds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_donations_fund_id",
                schema: "giving",
                table: "donations",
                column: "fund_id");

            migrationBuilder.CreateIndex(
                name: "ix_donations_tenant_id_contact_id",
                schema: "giving",
                table: "donations",
                columns: new[] { "tenant_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_donations_tenant_id_receipt_number",
                schema: "giving",
                table: "donations",
                columns: new[] { "tenant_id", "receipt_number" },
                unique: true,
                filter: "receipt_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_donations_tenant_id_received_on",
                schema: "giving",
                table: "donations",
                columns: new[] { "tenant_id", "received_on" });

            migrationBuilder.CreateIndex(
                name: "ix_funds_tenant_id_name",
                schema: "giving",
                table: "funds",
                columns: new[] { "tenant_id", "name" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_settings_tenant_id",
                schema: "giving",
                table: "receipt_settings",
                column: "tenant_id",
                unique: true);

            // Makbuz numarası sırası: yıl başına bir satır, artırma satır kilidiyle yapılır.
            migrationBuilder.Sql("""
                CREATE TABLE giving.receipt_sequences (
                    tenant_id uuid NOT NULL,
                    year integer NOT NULL,
                    last_number bigint NOT NULL,
                    CONSTRAINT pk_receipt_sequences PRIMARY KEY (tenant_id, year)
                );
                ALTER TABLE giving.donations ENABLE ROW LEVEL SECURITY;
                ALTER TABLE giving.donations FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON giving.donations
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE giving.funds ENABLE ROW LEVEL SECURITY;
                ALTER TABLE giving.funds FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON giving.funds
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE giving.receipt_settings ENABLE ROW LEVEL SECURITY;
                ALTER TABLE giving.receipt_settings FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON giving.receipt_settings
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE giving.receipt_sequences ENABLE ROW LEVEL SECURITY;
                ALTER TABLE giving.receipt_sequences FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON giving.receipt_sequences
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE giving.receipt_sequences;");

            migrationBuilder.DropTable(
                name: "donations",
                schema: "giving");

            migrationBuilder.DropTable(
                name: "receipt_settings",
                schema: "giving");

            migrationBuilder.DropTable(
                name: "funds",
                schema: "giving");
        }
    }
}
