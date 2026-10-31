using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arma.Modules.Contacts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TagsGroupsRelationshipsAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "addresses",
                schema: "contacts",
                table: "contacts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "birth_date",
                schema: "contacts",
                table: "contacts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "merged_into_id",
                schema: "contacts",
                table: "contacts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "website",
                schema: "contacts",
                table: "contacts",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "groups",
                schema: "contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false, collation: "tr-TR-x-icu"),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "relationships",
                schema: "contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<short>(type: "smallint", nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_relationships", x => x.id);
                    table.ForeignKey(
                        name: "fk_relationships_contacts_from_contact_id",
                        column: x => x.from_contact_id,
                        principalSchema: "contacts",
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_relationships_contacts_to_contact_id",
                        column: x => x.to_contact_id,
                        principalSchema: "contacts",
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tags",
                schema: "contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false, collation: "tr-TR-x-icu"),
                    color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "group_members",
                schema: "contacts",
                columns: table => new
                {
                    groups_id = table.Column<Guid>(type: "uuid", nullable: false),
                    members_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_members", x => new { x.groups_id, x.members_id });
                    table.ForeignKey(
                        name: "fk_group_members_contacts_members_id",
                        column: x => x.members_id,
                        principalSchema: "contacts",
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_group_members_groups_groups_id",
                        column: x => x.groups_id,
                        principalSchema: "contacts",
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contact_tags",
                schema: "contacts",
                columns: table => new
                {
                    contacts_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tags_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_tags", x => new { x.contacts_id, x.tags_id });
                    table.ForeignKey(
                        name: "fk_contact_tags_contacts_contacts_id",
                        column: x => x.contacts_id,
                        principalSchema: "contacts",
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_contact_tags_tags_tags_id",
                        column: x => x.tags_id,
                        principalSchema: "contacts",
                        principalTable: "tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_id_national_id",
                schema: "contacts",
                table: "contacts",
                columns: new[] { "tenant_id", "national_id" },
                unique: true,
                filter: "national_id IS NOT NULL AND NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_contact_tags_tags_id",
                schema: "contacts",
                table: "contact_tags",
                column: "tags_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_members_members_id",
                schema: "contacts",
                table: "group_members",
                column: "members_id");

            migrationBuilder.CreateIndex(
                name: "ix_groups_tenant_id_name",
                schema: "contacts",
                table: "groups",
                columns: new[] { "tenant_id", "name" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_relationships_from_contact_id",
                schema: "contacts",
                table: "relationships",
                column: "from_contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_relationships_to_contact_id",
                schema: "contacts",
                table: "relationships",
                column: "to_contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_tags_tenant_id_name",
                schema: "contacts",
                table: "tags",
                columns: new[] { "tenant_id", "name" },
                unique: true,
                filter: "NOT is_deleted");

            migrationBuilder.Sql("""
                ALTER TABLE contacts.tags ENABLE ROW LEVEL SECURITY;
                ALTER TABLE contacts.tags FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON contacts.tags
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE contacts.groups ENABLE ROW LEVEL SECURITY;
                ALTER TABLE contacts.groups FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON contacts.groups
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                ALTER TABLE contacts.relationships ENABLE ROW LEVEL SECURITY;
                ALTER TABLE contacts.relationships FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON contacts.relationships
                    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_tags",
                schema: "contacts");

            migrationBuilder.DropTable(
                name: "group_members",
                schema: "contacts");

            migrationBuilder.DropTable(
                name: "relationships",
                schema: "contacts");

            migrationBuilder.DropTable(
                name: "tags",
                schema: "contacts");

            migrationBuilder.DropTable(
                name: "groups",
                schema: "contacts");

            migrationBuilder.DropIndex(
                name: "ix_contacts_tenant_id_national_id",
                schema: "contacts",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "addresses",
                schema: "contacts",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "birth_date",
                schema: "contacts",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "merged_into_id",
                schema: "contacts",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "website",
                schema: "contacts",
                table: "contacts");
        }
    }
}
