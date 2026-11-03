using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arma.Modules.Contacts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EncryptNationalId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_contacts_tenant_id_national_id",
                schema: "contacts",
                table: "contacts");

            migrationBuilder.AlterColumn<string>(
                name: "national_id",
                schema: "contacts",
                table: "contacts",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(11)",
                oldMaxLength: 11,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "national_id_hash",
                schema: "contacts",
                table: "contacts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_id_national_id_hash",
                schema: "contacts",
                table: "contacts",
                columns: new[] { "tenant_id", "national_id_hash" },
                unique: true,
                filter: "national_id_hash IS NOT NULL AND NOT is_deleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Geri almadan önce şifreli kimlik numaraları çözülmelidir; şifreli değer 11 karaktere sığmaz.
            migrationBuilder.DropIndex(
                name: "ix_contacts_tenant_id_national_id_hash",
                schema: "contacts",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "national_id_hash",
                schema: "contacts",
                table: "contacts");

            migrationBuilder.AlterColumn<string>(
                name: "national_id",
                schema: "contacts",
                table: "contacts",
                type: "character varying(11)",
                maxLength: 11,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_id_national_id",
                schema: "contacts",
                table: "contacts",
                columns: new[] { "tenant_id", "national_id" },
                unique: true,
                filter: "national_id IS NOT NULL AND NOT is_deleted");
        }
    }
}
