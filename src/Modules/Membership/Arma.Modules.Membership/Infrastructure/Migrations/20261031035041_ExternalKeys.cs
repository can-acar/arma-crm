using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arma.Modules.Membership.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExternalKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "external_key",
                schema: "membership",
                table: "members",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_members_tenant_id_external_key",
                schema: "membership",
                table: "members",
                columns: new[] { "tenant_id", "external_key" },
                unique: true,
                filter: "external_key IS NOT NULL AND NOT is_deleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_members_tenant_id_external_key",
                schema: "membership",
                table: "members");

            migrationBuilder.DropColumn(
                name: "external_key",
                schema: "membership",
                table: "members");
        }
    }
}
