using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arma.Modules.Contacts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExternalKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "external_key",
                schema: "contacts",
                table: "contacts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_id_external_key",
                schema: "contacts",
                table: "contacts",
                columns: new[] { "tenant_id", "external_key" },
                unique: true,
                filter: "external_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_contacts_tenant_id_external_key",
                schema: "contacts",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "external_key",
                schema: "contacts",
                table: "contacts");
        }
    }
}
