using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arma.Modules.Cases.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cases");

            migrationBuilder.CreateTable(
                name: "case_types",
                schema: "cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "tr-TR-x-icu"),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    steps = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cases",
                schema: "cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    priority = table.Column<short>(type: "smallint", nullable: false),
                    assignee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    opened_on = table.Column<DateOnly>(type: "date", nullable: false),
                    closed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    outcome = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cases", x => x.id);
                    table.ForeignKey(
                        name: "fk_cases_case_types_type_id",
                        column: x => x.type_id,
                        principalSchema: "cases",
                        principalTable: "case_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "case_notes",
                schema: "cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    is_confidential = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_case_notes_cases_case_id",
                        column: x => x.case_id,
                        principalSchema: "cases",
                        principalTable: "cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "case_steps",
                schema: "cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    order = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_case_steps_cases_case_id",
                        column: x => x.case_id,
                        principalSchema: "cases",
                        principalTable: "cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_case_notes_case_id",
                schema: "cases",
                table: "case_notes",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_notes_tenant_id_case_id",
                schema: "cases",
                table: "case_notes",
                columns: new[] { "tenant_id", "case_id" });

            migrationBuilder.CreateIndex(
                name: "ix_case_steps_case_id",
                schema: "cases",
                table: "case_steps",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_steps_tenant_id_due_on",
                schema: "cases",
                table: "case_steps",
                columns: new[] { "tenant_id", "due_on" },
                filter: "completed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_case_types_tenant_id_name",
                schema: "cases",
                table: "case_types",
                columns: new[] { "tenant_id", "name" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_cases_tenant_id_contact_id",
                schema: "cases",
                table: "cases",
                columns: new[] { "tenant_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cases_tenant_id_number",
                schema: "cases",
                table: "cases",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cases_tenant_id_status_assignee_id",
                schema: "cases",
                table: "cases",
                columns: new[] { "tenant_id", "status", "assignee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cases_type_id",
                schema: "cases",
                table: "cases",
                column: "type_id");

            migrationBuilder.Sql("""
                CREATE TABLE cases.case_sequences (
                    tenant_id uuid NOT NULL,
                    year integer NOT NULL,
                    last_number bigint NOT NULL,
                    PRIMARY KEY (tenant_id, year)
                );
                ALTER TABLE cases.case_types ENABLE ROW LEVEL SECURITY;
                ALTER TABLE cases.case_types FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON cases.case_types
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE cases.cases ENABLE ROW LEVEL SECURITY;
                ALTER TABLE cases.cases FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON cases.cases
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE cases.case_steps ENABLE ROW LEVEL SECURITY;
                ALTER TABLE cases.case_steps FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON cases.case_steps
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE cases.case_notes ENABLE ROW LEVEL SECURITY;
                ALTER TABLE cases.case_notes FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON cases.case_notes
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE cases.case_sequences ENABLE ROW LEVEL SECURITY;
                ALTER TABLE cases.case_sequences FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON cases.case_sequences
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE cases.case_sequences;");

            migrationBuilder.DropTable(
                name: "case_notes",
                schema: "cases");

            migrationBuilder.DropTable(
                name: "case_steps",
                schema: "cases");

            migrationBuilder.DropTable(
                name: "cases",
                schema: "cases");

            migrationBuilder.DropTable(
                name: "case_types",
                schema: "cases");
        }
    }
}
