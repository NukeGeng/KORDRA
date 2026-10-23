using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kordra.Modules.ProjectEnvironment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddResourcePolicyBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "policy_revision",
                schema: "app",
                table: "prj_environments",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.CreateTable(
                name: "prj_policy_bindings",
                schema: "app",
                columns: table => new
                {
                    binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    binding_revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prj_policy_bindings", x => x.binding_id);
                    table.UniqueConstraint("AK_prj_policy_bindings_binding_id_tenant_id_project_id_scope_k~", x => new { x.binding_id, x.tenant_id, x.project_id, x.scope_kind, x.action });
                    table.CheckConstraint("ck_prj_policy_binding", "binding_revision > 0 AND policy_revision > 0 AND action = 'target.read' AND policy_id <> '00000000-0000-0000-0000-000000000000' AND ((scope_kind = 'environment' AND environment_id IS NOT NULL) OR (scope_kind = 'project_without_environment' AND environment_id IS NULL))");
                    table.ForeignKey(
                        name: "FK_prj_policy_bindings_prj_environments_tenant_id_project_id_e~",
                        columns: x => new { x.tenant_id, x.project_id, x.environment_id },
                        principalSchema: "app",
                        principalTable: "prj_environments",
                        principalColumns: new[] { "tenant_id", "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_prj_policy_bindings_prj_projects_tenant_id_project_id",
                        columns: x => new { x.tenant_id, x.project_id },
                        principalSchema: "app",
                        principalTable: "prj_projects",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "prj_policy_binding_history",
                schema: "app",
                columns: table => new
                {
                    binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    binding_revision = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    manifest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_hash = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prj_policy_binding_history", x => new { x.binding_id, x.binding_revision });
                    table.CheckConstraint("ck_prj_policy_binding_history", "binding_revision > 0 AND policy_revision > 0 AND octet_length(content_hash) = 32 AND action = 'target.read' AND ((scope_kind = 'environment' AND environment_id IS NOT NULL) OR (scope_kind = 'project_without_environment' AND environment_id IS NULL))");
                    table.ForeignKey(
                        name: "FK_prj_policy_binding_history_prj_policy_bindings_binding_id_t~",
                        columns: x => new { x.binding_id, x.tenant_id, x.project_id, x.scope_kind, x.action },
                        principalSchema: "app",
                        principalTable: "prj_policy_bindings",
                        principalColumns: new[] { "binding_id", "tenant_id", "project_id", "scope_kind", "action" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_prj_policy_binding_history_binding_id_tenant_id_project_id_~",
                schema: "app",
                table: "prj_policy_binding_history",
                columns: new[] { "binding_id", "tenant_id", "project_id", "scope_kind", "action" });

            migrationBuilder.CreateIndex(
                name: "IX_prj_policy_bindings_tenant_id_project_id_action",
                schema: "app",
                table: "prj_policy_bindings",
                columns: new[] { "tenant_id", "project_id", "action" },
                unique: true,
                filter: "scope_kind = 'project_without_environment'");

            migrationBuilder.CreateIndex(
                name: "IX_prj_policy_bindings_tenant_id_project_id_environment_id_act~",
                schema: "app",
                table: "prj_policy_bindings",
                columns: new[] { "tenant_id", "project_id", "environment_id", "action" },
                unique: true,
                filter: "scope_kind = 'environment'");
            migrationBuilder.Sql("""
                DO $grants$ BEGIN
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'kordra_app') THEN
                REVOKE ALL ON TABLE app.prj_policy_bindings FROM PUBLIC; REVOKE ALL ON TABLE app.prj_policy_bindings FROM kordra_app; GRANT SELECT, INSERT ON TABLE app.prj_policy_bindings TO kordra_app;
                REVOKE ALL ON TABLE app.prj_policy_binding_history FROM PUBLIC; REVOKE ALL ON TABLE app.prj_policy_binding_history FROM kordra_app; GRANT SELECT, INSERT ON TABLE app.prj_policy_binding_history TO kordra_app;
                END IF;
                END $grants$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Authority history and clock floors are additive; use a reviewed forward correction.");
    }
}
