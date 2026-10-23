using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kordra.Modules.TenantManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialTenantPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "ten_commands",
                schema: "app",
                columns: table => new
                {
                    key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ten_commands", x => x.key_hash);
                });

            migrationBuilder.CreateTable(
                name: "ten_organizations",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ten_organizations", x => x.id);
                    table.CheckConstraint("ck_ten_org_revision", "revision > 0");
                    table.CheckConstraint("ck_ten_org_status", "status IN ('active','suspended')");
                });

            migrationBuilder.CreateTable(
                name: "ten_memberships",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ten_memberships", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_ten_member_revision", "revision > 0");
                    table.CheckConstraint("ck_ten_member_status", "status IN ('active','revoked')");
                    table.ForeignKey(
                        name: "FK_ten_memberships_ten_organizations_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "ten_organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ten_assignments",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_revision = table.Column<long>(type: "bigint", nullable: false),
                    scope_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    is_founding = table.Column<bool>(type: "boolean", nullable: false),
                    grantor_principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provenance_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ten_assignments", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_ten_assignment_revision", "revision > 0 AND definition_revision > 0");
                    table.CheckConstraint("ck_ten_assignment_status", "status IN ('active','revoked')");
                    table.CheckConstraint("ck_ten_scope", "scope_kind IN ('organization','project','environment')");
                    table.ForeignKey(
                        name: "FK_ten_assignments_ten_memberships_tenant_id_membership_id",
                        columns: x => new { x.tenant_id, x.membership_id },
                        principalSchema: "app",
                        principalTable: "ten_memberships",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ten_assignments_tenant_id_membership_id_scope_kind_scope_id",
                schema: "app",
                table: "ten_assignments",
                columns: new[] { "tenant_id", "membership_id", "scope_kind", "scope_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ten_memberships_tenant_id_principal_id",
                schema: "app",
                table: "ten_memberships",
                columns: new[] { "tenant_id", "principal_id" },
                unique: true);
            migrationBuilder.Sql("REVOKE ALL PRIVILEGES ON TABLE app.ten_ef_migrations_history FROM PUBLIC, kordra_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Foundation authority history is additive; use a reviewed forward correction.");
    }
}
