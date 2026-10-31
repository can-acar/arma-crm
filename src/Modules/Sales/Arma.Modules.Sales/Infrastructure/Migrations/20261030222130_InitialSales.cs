using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arma.Modules.Sales.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sales");

            migrationBuilder.CreateTable(
                name: "pipelines",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pipelines", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quote_settings",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    legal_info = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    default_terms = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    validity_days = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quote_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stages",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pipeline_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    probability = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stages", x => x.id);
                    table.ForeignKey(
                        name: "fk_stages_pipelines_pipeline_id",
                        column: x => x.pipeline_id,
                        principalSchema: "sales",
                        principalTable: "pipelines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "opportunities",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pipeline_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    expected_close_on = table.Column<DateOnly>(type: "date", nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    closed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    lost_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    sort_key = table.Column<double>(type: "double precision", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_opportunities", x => x.id);
                    table.ForeignKey(
                        name: "fk_opportunities_pipelines_pipeline_id",
                        column: x => x.pipeline_id,
                        principalSchema: "sales",
                        principalTable: "pipelines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_opportunities_stages_stage_id",
                        column: x => x.stage_id,
                        principalSchema: "sales",
                        principalTable: "stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "quotes",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    terms = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quotes", x => x.id);
                    table.ForeignKey(
                        name: "fk_quotes_opportunities_opportunity_id",
                        column: x => x.opportunity_id,
                        principalSchema: "sales",
                        principalTable: "opportunities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "quote_lines",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    quote_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_quote_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_quote_lines_quotes_quote_id",
                        column: x => x.quote_id,
                        principalSchema: "sales",
                        principalTable: "quotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_opportunities_pipeline_id_stage_id_sort_key",
                schema: "sales",
                table: "opportunities",
                columns: new[] { "pipeline_id", "stage_id", "sort_key" });

            migrationBuilder.CreateIndex(
                name: "ix_opportunities_stage_id",
                schema: "sales",
                table: "opportunities",
                column: "stage_id");

            migrationBuilder.CreateIndex(
                name: "ix_opportunities_tenant_id_contact_id",
                schema: "sales",
                table: "opportunities",
                columns: new[] { "tenant_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_quote_lines_quote_id",
                schema: "sales",
                table: "quote_lines",
                column: "quote_id");

            migrationBuilder.CreateIndex(
                name: "ix_quote_settings_tenant_id",
                schema: "sales",
                table: "quote_settings",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_quotes_opportunity_id",
                schema: "sales",
                table: "quotes",
                column: "opportunity_id");

            migrationBuilder.CreateIndex(
                name: "ix_quotes_tenant_id_contact_id",
                schema: "sales",
                table: "quotes",
                columns: new[] { "tenant_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_quotes_tenant_id_number",
                schema: "sales",
                table: "quotes",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stages_pipeline_id",
                schema: "sales",
                table: "stages",
                column: "pipeline_id");

            migrationBuilder.Sql("""
                CREATE TABLE sales.quote_sequences (
                    tenant_id uuid NOT NULL,
                    year integer NOT NULL,
                    last_number bigint NOT NULL,
                    PRIMARY KEY (tenant_id, year)
                );
                ALTER TABLE sales.pipelines ENABLE ROW LEVEL SECURITY;
                ALTER TABLE sales.pipelines FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON sales.pipelines
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE sales.stages ENABLE ROW LEVEL SECURITY;
                ALTER TABLE sales.stages FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON sales.stages
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE sales.opportunities ENABLE ROW LEVEL SECURITY;
                ALTER TABLE sales.opportunities FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON sales.opportunities
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE sales.quotes ENABLE ROW LEVEL SECURITY;
                ALTER TABLE sales.quotes FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON sales.quotes
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE sales.quote_lines ENABLE ROW LEVEL SECURITY;
                ALTER TABLE sales.quote_lines FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON sales.quote_lines
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE sales.quote_settings ENABLE ROW LEVEL SECURITY;
                ALTER TABLE sales.quote_settings FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON sales.quote_settings
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE sales.quote_sequences ENABLE ROW LEVEL SECURITY;
                ALTER TABLE sales.quote_sequences FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON sales.quote_sequences
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE sales.quote_sequences;");

            migrationBuilder.DropTable(
                name: "quote_lines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "quote_settings",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "quotes",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "opportunities",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "stages",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "pipelines",
                schema: "sales");
        }
    }
}
