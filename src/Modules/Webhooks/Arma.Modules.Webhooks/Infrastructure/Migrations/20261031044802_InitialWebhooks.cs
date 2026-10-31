using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arma.Modules.Webhooks.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "webhooks");

            migrationBuilder.CreateTable(
                name: "endpoints",
                schema: "webhooks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    protected_secret = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    events = table.Column<List<string>>(type: "text[]", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_endpoints", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "deliveries",
                schema: "webhooks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    payload = table.Column<string>(type: "json", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_status_code = table.Column<int>(type: "integer", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "fk_deliveries_endpoints_endpoint_id",
                        column: x => x.endpoint_id,
                        principalSchema: "webhooks",
                        principalTable: "endpoints",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_endpoint_id",
                schema: "webhooks",
                table: "deliveries",
                column: "endpoint_id");

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_status_next_attempt_at",
                schema: "webhooks",
                table: "deliveries",
                columns: new[] { "status", "next_attempt_at" },
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_tenant_id_endpoint_id_created_at",
                schema: "webhooks",
                table: "deliveries",
                columns: new[] { "tenant_id", "endpoint_id", "created_at" });

            migrationBuilder.Sql("""
                ALTER TABLE webhooks.endpoints ENABLE ROW LEVEL SECURITY;
                ALTER TABLE webhooks.endpoints FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON webhooks.endpoints
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE webhooks.deliveries ENABLE ROW LEVEL SECURITY;
                ALTER TABLE webhooks.deliveries FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON webhooks.deliveries
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deliveries",
                schema: "webhooks");

            migrationBuilder.DropTable(
                name: "endpoints",
                schema: "webhooks");
        }
    }
}
