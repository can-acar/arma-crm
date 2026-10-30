using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arma.Modules.Activities.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialActivities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "activities");

            migrationBuilder.CreateTable(
                name: "activities",
                schema: "activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    assigned_to_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_module = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    source_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "activity_participants",
                schema: "activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_participants", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_participants_activities_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "activities",
                        principalTable: "activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activities_tenant_id_assigned_to_user_id_status",
                schema: "activities",
                table: "activities",
                columns: new[] { "tenant_id", "assigned_to_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_activities_tenant_id_source_module_source_id",
                schema: "activities",
                table: "activities",
                columns: new[] { "tenant_id", "source_module", "source_id" },
                unique: true,
                filter: "source_module IS NOT NULL AND NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_activity_participants_activity_id_contact_id",
                schema: "activities",
                table: "activity_participants",
                columns: new[] { "activity_id", "contact_id" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_activity_participants_contact_id",
                schema: "activities",
                table: "activity_participants",
                column: "contact_id");

            migrationBuilder.Sql("""
                ALTER TABLE activities.activities ENABLE ROW LEVEL SECURITY;
                ALTER TABLE activities.activities FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON activities.activities
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE activities.activity_participants ENABLE ROW LEVEL SECURITY;
                ALTER TABLE activities.activity_participants FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON activities.activity_participants
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_participants",
                schema: "activities");

            migrationBuilder.DropTable(
                name: "activities",
                schema: "activities");
        }
    }
}
