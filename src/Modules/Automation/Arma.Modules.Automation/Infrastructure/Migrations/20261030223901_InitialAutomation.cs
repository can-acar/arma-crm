using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arma.Modules.Automation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "automation");

            migrationBuilder.CreateTable(
                name: "rules",
                schema: "automation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    trigger = table.Column<short>(type: "smallint", nullable: false),
                    segment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_module = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    subject_contains = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    include_existing = table.Column<bool>(type: "boolean", nullable: false),
                    baseline_pending = table.Column<bool>(type: "boolean", nullable: false),
                    actions_json = table.Column<string>(type: "jsonb", nullable: false),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "runs",
                schema: "automation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    ran_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_runs_rules_rule_id",
                        column: x => x.rule_id,
                        principalSchema: "automation",
                        principalTable: "rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_runs_rule_id",
                schema: "automation",
                table: "runs",
                column: "rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_runs_tenant_id_contact_id",
                schema: "automation",
                table: "runs",
                columns: new[] { "tenant_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_runs_tenant_id_rule_id_contact_id_key",
                schema: "automation",
                table: "runs",
                columns: new[] { "tenant_id", "rule_id", "contact_id", "key" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE automation.rules ENABLE ROW LEVEL SECURITY;
                ALTER TABLE automation.rules FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON automation.rules
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE automation.runs ENABLE ROW LEVEL SECURITY;
                ALTER TABLE automation.runs FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON automation.runs
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "runs",
                schema: "automation");

            migrationBuilder.DropTable(
                name: "rules",
                schema: "automation");
        }
    }
}
