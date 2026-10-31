using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arma.Modules.Events.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "events");

            migrationBuilder.CreateTable(
                name: "events",
                schema: "events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: true),
                    registration_closes_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    confirmation_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ticket_types",
                schema: "events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("pk_ticket_types", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticket_types_events_event_id",
                        column: x => x.event_id,
                        principalSchema: "events",
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "registrations",
                schema: "events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    payment = table.Column<short>(type: "smallint", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    checked_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reminder_sent = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registrations", x => x.id);
                    table.ForeignKey(
                        name: "fk_registrations_events_event_id",
                        column: x => x.event_id,
                        principalSchema: "events",
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_registrations_ticket_types_ticket_type_id",
                        column: x => x.ticket_type_id,
                        principalSchema: "events",
                        principalTable: "ticket_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_events_tenant_id_slug",
                schema: "events",
                table: "events",
                columns: new[] { "tenant_id", "slug" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_events_tenant_id_starts_at",
                schema: "events",
                table: "events",
                columns: new[] { "tenant_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_registrations_event_id_status",
                schema: "events",
                table: "registrations",
                columns: new[] { "event_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_registrations_tenant_id_code",
                schema: "events",
                table: "registrations",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_registrations_tenant_id_contact_id",
                schema: "events",
                table: "registrations",
                columns: new[] { "tenant_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_registrations_ticket_type_id",
                schema: "events",
                table: "registrations",
                column: "ticket_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_types_event_id",
                schema: "events",
                table: "ticket_types",
                column: "event_id");

            migrationBuilder.Sql("""
                ALTER TABLE events.events ENABLE ROW LEVEL SECURITY;
                ALTER TABLE events.events FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON events.events
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE events.ticket_types ENABLE ROW LEVEL SECURITY;
                ALTER TABLE events.ticket_types FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON events.ticket_types
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE events.registrations ENABLE ROW LEVEL SECURITY;
                ALTER TABLE events.registrations FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON events.registrations
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "registrations",
                schema: "events");

            migrationBuilder.DropTable(
                name: "ticket_types",
                schema: "events");

            migrationBuilder.DropTable(
                name: "events",
                schema: "events");
        }
    }
}
