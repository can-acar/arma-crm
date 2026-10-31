using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arma.Modules.Contacts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CustomFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "custom_fields_json",
                schema: "contacts",
                table: "contacts",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.CreateTable(
                name: "custom_fields",
                schema: "contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "tr-TR-x-icu"),
                    type = table.Column<short>(type: "smallint", nullable: false),
                    options_text = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    applies_to = table.Column<short>(type: "smallint", nullable: true),
                    help_text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    order = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_custom_fields", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_custom_fields_tenant_id_label",
                schema: "contacts",
                table: "custom_fields",
                columns: new[] { "tenant_id", "label" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.Sql("""
                ALTER TABLE contacts.custom_fields ENABLE ROW LEVEL SECURITY;
                ALTER TABLE contacts.custom_fields FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON contacts.custom_fields
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "custom_fields",
                schema: "contacts");

            migrationBuilder.DropColumn(
                name: "custom_fields_json",
                schema: "contacts",
                table: "contacts");
        }
    }
}
