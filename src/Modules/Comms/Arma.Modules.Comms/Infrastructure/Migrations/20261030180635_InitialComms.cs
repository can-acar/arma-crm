using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arma.Modules.Comms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialComms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "comms");

            migrationBuilder.CreateTable(
                name: "channel_consents",
                schema: "comms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    address = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    iys_state = table.Column<short>(type: "smallint", nullable: false),
                    iys_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channel_consents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mailings",
                schema: "comms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    channel = table.Column<short>(type: "smallint", nullable: false),
                    subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    body = table.Column<string>(type: "text", nullable: false),
                    is_commercial = table.Column<bool>(type: "boolean", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mailings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                schema: "comms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mailing_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel = table.Column<short>(type: "smallint", nullable: false),
                    to = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    body = table.Column<string>(type: "text", nullable: false),
                    is_commercial = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    provider_message_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "templates",
                schema: "comms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    channel = table.Column<short>(type: "smallint", nullable: false),
                    subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    body = table.Column<string>(type: "text", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_templates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_channel_consents_iys_state",
                schema: "comms",
                table: "channel_consents",
                column: "iys_state",
                filter: "iys_state IN (1, 3)");

            migrationBuilder.CreateIndex(
                name: "ix_channel_consents_tenant_id_contact_id_channel",
                schema: "comms",
                table: "channel_consents",
                columns: new[] { "tenant_id", "contact_id", "channel" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_messages_mailing_id_status",
                schema: "comms",
                table: "messages",
                columns: new[] { "mailing_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_tenant_id_contact_id",
                schema: "comms",
                table: "messages",
                columns: new[] { "tenant_id", "contact_id" });

            migrationBuilder.Sql("""
                ALTER TABLE comms.channel_consents ENABLE ROW LEVEL SECURITY;
                ALTER TABLE comms.channel_consents FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON comms.channel_consents
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE comms.messages ENABLE ROW LEVEL SECURITY;
                ALTER TABLE comms.messages FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON comms.messages
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE comms.mailings ENABLE ROW LEVEL SECURITY;
                ALTER TABLE comms.mailings FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON comms.mailings
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE comms.templates ENABLE ROW LEVEL SECURITY;
                ALTER TABLE comms.templates FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON comms.templates
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "channel_consents",
                schema: "comms");

            migrationBuilder.DropTable(
                name: "mailings",
                schema: "comms");

            migrationBuilder.DropTable(
                name: "messages",
                schema: "comms");

            migrationBuilder.DropTable(
                name: "templates",
                schema: "comms");
        }
    }
}
