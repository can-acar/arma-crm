using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arma.Modules.Privacy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPrivacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "privacy");

            migrationBuilder.CreateTable(
                name: "anonymizations",
                schema: "privacy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    performed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_anonymizations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "consent_records",
                schema: "privacy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notice_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    granted = table.Column<bool>(type: "boolean", nullable: false),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consent_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "data_requests",
                schema: "privacy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<short>(type: "smallint", nullable: false),
                    channel = table.Column<short>(type: "smallint", nullable: false),
                    requester_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    description = table.Column<string>(type: "text", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    identity_verified = table.Column<bool>(type: "boolean", nullable: false),
                    response = table.Column<string>(type: "text", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notice_versions",
                schema: "privacy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    notice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    requires_consent = table.Column<bool>(type: "boolean", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notice_versions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notices",
                schema: "privacy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    requires_consent = table.Column<bool>(type: "boolean", nullable: false),
                    draft_text = table.Column<string>(type: "text", nullable: false),
                    current_version = table.Column<int>(type: "integer", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    has_unpublished_changes = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notices", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_anonymizations_tenant_id_contact_id",
                schema: "privacy",
                table: "anonymizations",
                columns: new[] { "tenant_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_consent_records_tenant_id_contact_id_notice_id_recorded_at",
                schema: "privacy",
                table: "consent_records",
                columns: new[] { "tenant_id", "contact_id", "notice_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_data_requests_tenant_id_contact_id",
                schema: "privacy",
                table: "data_requests",
                columns: new[] { "tenant_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_data_requests_tenant_id_status_due_on",
                schema: "privacy",
                table: "data_requests",
                columns: new[] { "tenant_id", "status", "due_on" });

            migrationBuilder.CreateIndex(
                name: "ix_notice_versions_notice_id_number",
                schema: "privacy",
                table: "notice_versions",
                columns: new[] { "notice_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notices_tenant_id_slug",
                schema: "privacy",
                table: "notices",
                columns: new[] { "tenant_id", "slug" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.Sql("""
                ALTER TABLE privacy.notices ENABLE ROW LEVEL SECURITY;
                ALTER TABLE privacy.notices FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON privacy.notices
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE privacy.notice_versions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE privacy.notice_versions FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON privacy.notice_versions
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE privacy.consent_records ENABLE ROW LEVEL SECURITY;
                ALTER TABLE privacy.consent_records FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON privacy.consent_records
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE privacy.data_requests ENABLE ROW LEVEL SECURITY;
                ALTER TABLE privacy.data_requests FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON privacy.data_requests
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE privacy.anonymizations ENABLE ROW LEVEL SECURITY;
                ALTER TABLE privacy.anonymizations FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON privacy.anonymizations
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                -- Rıza kayıtları ve yayımlanmış sürümler ispat niteliğindedir: değiştirilemez, silinemez.
                CREATE FUNCTION privacy.reject_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'KVKK kaydı değiştirilemez (%).', TG_TABLE_NAME;
                END $$;
                CREATE TRIGGER consent_records_immutable BEFORE UPDATE OR DELETE ON privacy.consent_records
                    FOR EACH ROW WHEN (current_setting('arma.allow_privacy_change', true) IS DISTINCT FROM 'on') EXECUTE FUNCTION privacy.reject_change();
                CREATE TRIGGER notice_versions_immutable BEFORE UPDATE OR DELETE ON privacy.notice_versions
                    FOR EACH ROW EXECUTE FUNCTION privacy.reject_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION privacy.reject_change() CASCADE;");

            migrationBuilder.DropTable(
                name: "anonymizations",
                schema: "privacy");

            migrationBuilder.DropTable(
                name: "consent_records",
                schema: "privacy");

            migrationBuilder.DropTable(
                name: "data_requests",
                schema: "privacy");

            migrationBuilder.DropTable(
                name: "notice_versions",
                schema: "privacy");

            migrationBuilder.DropTable(
                name: "notices",
                schema: "privacy");
        }
    }
}
