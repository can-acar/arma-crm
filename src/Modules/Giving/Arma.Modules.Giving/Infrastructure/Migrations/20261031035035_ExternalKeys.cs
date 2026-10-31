using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arma.Modules.Giving.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExternalKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "external_key",
                schema: "giving",
                table: "donations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_donations_tenant_id_external_key",
                schema: "giving",
                table: "donations",
                columns: new[] { "tenant_id", "external_key" },
                unique: true,
                filter: "external_key IS NOT NULL AND NOT is_deleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_donations_tenant_id_external_key",
                schema: "giving",
                table: "donations");

            migrationBuilder.DropColumn(
                name: "external_key",
                schema: "giving",
                table: "donations");
        }
    }
}
