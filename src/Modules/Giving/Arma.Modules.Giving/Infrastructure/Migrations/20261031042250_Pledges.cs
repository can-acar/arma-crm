using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arma.Modules.Giving.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Pledges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "pledge_id",
                schema: "giving",
                table: "donations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pledges",
                schema: "giving",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fund_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    frequency = table.Column<short>(type: "smallint", nullable: false),
                    installment_count = table.Column<int>(type: "integer", nullable: false),
                    start_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pledges", x => x.id);
                    table.ForeignKey(
                        name: "fk_pledges_funds_fund_id",
                        column: x => x.fund_id,
                        principalSchema: "giving",
                        principalTable: "funds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_donations_pledge_id",
                schema: "giving",
                table: "donations",
                column: "pledge_id");

            migrationBuilder.CreateIndex(
                name: "ix_donations_tenant_id_pledge_id",
                schema: "giving",
                table: "donations",
                columns: new[] { "tenant_id", "pledge_id" },
                filter: "pledge_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pledges_fund_id",
                schema: "giving",
                table: "pledges",
                column: "fund_id");

            migrationBuilder.CreateIndex(
                name: "ix_pledges_tenant_id_contact_id",
                schema: "giving",
                table: "pledges",
                columns: new[] { "tenant_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pledges_tenant_id_status",
                schema: "giving",
                table: "pledges",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.AddForeignKey(
                name: "fk_donations_pledges_pledge_id",
                schema: "giving",
                table: "donations",
                column: "pledge_id",
                principalSchema: "giving",
                principalTable: "pledges",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                ALTER TABLE giving.pledges ENABLE ROW LEVEL SECURITY;
                ALTER TABLE giving.pledges FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON giving.pledges
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_donations_pledges_pledge_id",
                schema: "giving",
                table: "donations");

            migrationBuilder.DropTable(
                name: "pledges",
                schema: "giving");

            migrationBuilder.DropIndex(
                name: "ix_donations_pledge_id",
                schema: "giving",
                table: "donations");

            migrationBuilder.DropIndex(
                name: "ix_donations_tenant_id_pledge_id",
                schema: "giving",
                table: "donations");

            migrationBuilder.DropColumn(
                name: "pledge_id",
                schema: "giving",
                table: "donations");
        }
    }
}
