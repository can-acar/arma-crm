using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arma.Modules.Contacts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "contacts");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "contacts",
                schema: "contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<short>(type: "smallint", nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    organization_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "tr-TR-x-icu"),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    national_id = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    search_text = table.Column<string>(type: "text", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contacts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_search_text",
                schema: "contacts",
                table: "contacts",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_id_display_name",
                schema: "contacts",
                table: "contacts",
                columns: new[] { "tenant_id", "display_name" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_id_email",
                schema: "contacts",
                table: "contacts",
                columns: new[] { "tenant_id", "email" });

            // Kurum yalıtımı: her bağlantı app.tenant_id değişkenini atar (TenantConnectionInterceptor).
            // FORCE, tablo sahibi olan uygulama kullanıcısını da politikaya tabi kılar.
            migrationBuilder.Sql("""
                ALTER TABLE contacts.contacts ENABLE ROW LEVEL SECURITY;
                ALTER TABLE contacts.contacts FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON contacts.contacts
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contacts",
                schema: "contacts");
        }
    }
}
