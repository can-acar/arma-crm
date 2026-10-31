using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arma.Modules.Membership.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "membership");

            migrationBuilder.CreateTable(
                name: "types",
                schema: "membership",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false, collation: "tr-TR-x-icu"),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    duration_months = table.Column<int>(type: "integer", nullable: false),
                    fee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    grace_days = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "members",
                schema: "membership",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<long>(type: "bigint", nullable: false),
                    joined_on = table.Column<DateOnly>(type: "date", nullable: false),
                    start_on = table.Column<DateOnly>(type: "date", nullable: false),
                    end_on = table.Column<DateOnly>(type: "date", nullable: true),
                    cancelled_on = table.Column<DateOnly>(type: "date", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    reminder_sent_for = table.Column<DateOnly>(type: "date", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_members_types_type_id",
                        column: x => x.type_id,
                        principalSchema: "membership",
                        principalTable: "types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "membership",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    method = table.Column<short>(type: "smallint", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_payments_members_member_id",
                        column: x => x.member_id,
                        principalSchema: "membership",
                        principalTable: "members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_members_tenant_id_contact_id",
                schema: "membership",
                table: "members",
                columns: new[] { "tenant_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_members_tenant_id_end_on",
                schema: "membership",
                table: "members",
                columns: new[] { "tenant_id", "end_on" });

            migrationBuilder.CreateIndex(
                name: "ix_members_tenant_id_number",
                schema: "membership",
                table: "members",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_members_type_id",
                schema: "membership",
                table: "members",
                column: "type_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_member_id",
                schema: "membership",
                table: "payments",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_paid_on",
                schema: "membership",
                table: "payments",
                columns: new[] { "tenant_id", "paid_on" });

            migrationBuilder.CreateIndex(
                name: "ix_types_tenant_id_name",
                schema: "membership",
                table: "types",
                columns: new[] { "tenant_id", "name" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.Sql("""
                CREATE TABLE membership.number_sequences (
                    tenant_id uuid NOT NULL,
                    last_number bigint NOT NULL,
                    CONSTRAINT pk_number_sequences PRIMARY KEY (tenant_id)
                );
                ALTER TABLE membership.types ENABLE ROW LEVEL SECURITY;
                ALTER TABLE membership.types FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON membership.types
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE membership.members ENABLE ROW LEVEL SECURITY;
                ALTER TABLE membership.members FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON membership.members
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE membership.payments ENABLE ROW LEVEL SECURITY;
                ALTER TABLE membership.payments FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON membership.payments
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE membership.number_sequences ENABLE ROW LEVEL SECURITY;
                ALTER TABLE membership.number_sequences FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON membership.number_sequences
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE membership.number_sequences;");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "membership");

            migrationBuilder.DropTable(
                name: "members",
                schema: "membership");

            migrationBuilder.DropTable(
                name: "types",
                schema: "membership");
        }
    }
}
