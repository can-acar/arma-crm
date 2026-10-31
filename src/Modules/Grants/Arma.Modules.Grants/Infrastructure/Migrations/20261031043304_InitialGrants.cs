using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arma.Modules.Grants.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "grants");

            migrationBuilder.CreateTable(
                name: "grants",
                schema: "grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    funder_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    program = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    requested = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    awarded = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    submitted_on = table.Column<DateOnly>(type: "date", nullable: true),
                    decided_on = table.Column<DateOnly>(type: "date", nullable: true),
                    start_on = table.Column<DateOnly>(type: "date", nullable: true),
                    end_on = table.Column<DateOnly>(type: "date", nullable: true),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "grant_milestones",
                schema: "grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    completed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grant_milestones", x => x.id);
                    table.ForeignKey(
                        name: "fk_grant_milestones_grants_grant_id",
                        column: x => x.grant_id,
                        principalSchema: "grants",
                        principalTable: "grants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_grant_milestones_grant_id",
                schema: "grants",
                table: "grant_milestones",
                column: "grant_id");

            migrationBuilder.CreateIndex(
                name: "ix_grant_milestones_tenant_id_due_on",
                schema: "grants",
                table: "grant_milestones",
                columns: new[] { "tenant_id", "due_on" },
                filter: "completed_on IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_grants_tenant_id_funder_id",
                schema: "grants",
                table: "grants",
                columns: new[] { "tenant_id", "funder_id" });

            migrationBuilder.CreateIndex(
                name: "ix_grants_tenant_id_status",
                schema: "grants",
                table: "grants",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.Sql("""
                ALTER TABLE grants.grants ENABLE ROW LEVEL SECURITY;
                ALTER TABLE grants.grants FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON grants.grants
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE grants.grant_milestones ENABLE ROW LEVEL SECURITY;
                ALTER TABLE grants.grant_milestones FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON grants.grant_milestones
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "grant_milestones",
                schema: "grants");

            migrationBuilder.DropTable(
                name: "grants",
                schema: "grants");
        }
    }
}
