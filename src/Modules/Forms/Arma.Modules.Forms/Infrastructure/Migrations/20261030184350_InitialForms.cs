using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arma.Modules.Forms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialForms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "forms");

            migrationBuilder.CreateTable(
                name: "forms",
                schema: "forms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    intro = table.Column<string>(type: "text", nullable: true),
                    success_message = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    fields = table.Column<string>(type: "jsonb", nullable: false),
                    tag_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ask_email_consent = table.Column<bool>(type: "boolean", nullable: false),
                    ask_sms_consent = table.Column<bool>(type: "boolean", nullable: false),
                    consent_notice_slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    info_notice_slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_forms", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "submissions",
                schema: "forms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    form_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    values = table.Column<string>(type: "jsonb", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submissions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_forms_tenant_id_slug",
                schema: "forms",
                table: "forms",
                columns: new[] { "tenant_id", "slug" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_submissions_form_id_submitted_at",
                schema: "forms",
                table: "submissions",
                columns: new[] { "form_id", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_submissions_tenant_id_contact_id",
                schema: "forms",
                table: "submissions",
                columns: new[] { "tenant_id", "contact_id" });

            migrationBuilder.Sql("""
                ALTER TABLE forms.forms ENABLE ROW LEVEL SECURITY;
                ALTER TABLE forms.forms FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON forms.forms
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE forms.submissions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE forms.submissions FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON forms.submissions
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "forms",
                schema: "forms");

            migrationBuilder.DropTable(
                name: "submissions",
                schema: "forms");
        }
    }
}
