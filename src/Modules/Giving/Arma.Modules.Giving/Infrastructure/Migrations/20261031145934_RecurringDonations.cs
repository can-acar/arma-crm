using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arma.Modules.Giving.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RecurringDonations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "payment_providers",
                schema: "giving",
                table: "receipt_settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "provider",
                schema: "giving",
                table: "donations",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "recurring_donation_id",
                schema: "giving",
                table: "donations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "recurring_donations",
                schema: "giving",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    fund_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    day_of_month = table.Column<int>(type: "integer", nullable: false),
                    next_charge_on = table.Column<DateOnly>(type: "date", nullable: true),
                    card_token = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    card_label = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    failure_count = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    last_charged_on = table.Column<DateOnly>(type: "date", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurring_donations", x => x.id);
                    table.ForeignKey(
                        name: "fk_recurring_donations_funds_fund_id",
                        column: x => x.fund_id,
                        principalSchema: "giving",
                        principalTable: "funds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_donations_recurring_donation_id",
                schema: "giving",
                table: "donations",
                column: "recurring_donation_id");

            migrationBuilder.CreateIndex(
                name: "ix_donations_tenant_id_recurring_donation_id",
                schema: "giving",
                table: "donations",
                columns: new[] { "tenant_id", "recurring_donation_id" },
                filter: "recurring_donation_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_donations_fund_id",
                schema: "giving",
                table: "recurring_donations",
                column: "fund_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_donations_tenant_id_contact_id",
                schema: "giving",
                table: "recurring_donations",
                columns: new[] { "tenant_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_recurring_donations_tenant_id_status_next_charge_on",
                schema: "giving",
                table: "recurring_donations",
                columns: new[] { "tenant_id", "status", "next_charge_on" });

            migrationBuilder.AddForeignKey(
                name: "fk_donations_recurring_donations_recurring_donation_id",
                schema: "giving",
                table: "donations",
                column: "recurring_donation_id",
                principalSchema: "giving",
                principalTable: "recurring_donations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                ALTER TABLE giving.recurring_donations ENABLE ROW LEVEL SECURITY;
                ALTER TABLE giving.recurring_donations FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON giving.recurring_donations
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_donations_recurring_donations_recurring_donation_id",
                schema: "giving",
                table: "donations");

            migrationBuilder.DropTable(
                name: "recurring_donations",
                schema: "giving");

            migrationBuilder.DropIndex(
                name: "ix_donations_recurring_donation_id",
                schema: "giving",
                table: "donations");

            migrationBuilder.DropIndex(
                name: "ix_donations_tenant_id_recurring_donation_id",
                schema: "giving",
                table: "donations");

            migrationBuilder.DropColumn(
                name: "payment_providers",
                schema: "giving",
                table: "receipt_settings");

            migrationBuilder.DropColumn(
                name: "recurring_donation_id",
                schema: "giving",
                table: "donations");

            migrationBuilder.AlterColumn<string>(
                name: "provider",
                schema: "giving",
                table: "donations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);
        }
    }
}
