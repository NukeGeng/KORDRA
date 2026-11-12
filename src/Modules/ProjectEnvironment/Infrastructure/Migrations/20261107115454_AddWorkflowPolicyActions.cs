using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kordra.Modules.ProjectEnvironment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowPolicyActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_prj_policy_binding",
                schema: "app",
                table: "prj_policy_bindings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_prj_policy_binding_history",
                schema: "app",
                table: "prj_policy_binding_history");

            migrationBuilder.AddCheckConstraint(
                name: "ck_prj_policy_binding",
                schema: "app",
                table: "prj_policy_bindings",
                sql: "binding_revision > 0 AND policy_revision > 0 AND (action = 'target.read' OR (action IN ('workflow.start','workflow.read','workflow.cancel','workflow.reconcile') AND scope_kind = 'environment' AND environment_id IS NOT NULL)) AND policy_id <> '00000000-0000-0000-0000-000000000000' AND ((scope_kind = 'environment' AND environment_id IS NOT NULL) OR (scope_kind = 'project_without_environment' AND environment_id IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_prj_policy_binding_history",
                schema: "app",
                table: "prj_policy_binding_history",
                sql: "binding_revision > 0 AND policy_revision > 0 AND octet_length(content_hash) = 32 AND (action = 'target.read' OR (action IN ('workflow.start','workflow.read','workflow.cancel','workflow.reconcile') AND scope_kind = 'environment' AND environment_id IS NOT NULL)) AND ((scope_kind = 'environment' AND environment_id IS NOT NULL) OR (scope_kind = 'project_without_environment' AND environment_id IS NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_prj_policy_binding",
                schema: "app",
                table: "prj_policy_bindings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_prj_policy_binding_history",
                schema: "app",
                table: "prj_policy_binding_history");

            migrationBuilder.AddCheckConstraint(
                name: "ck_prj_policy_binding",
                schema: "app",
                table: "prj_policy_bindings",
                sql: "binding_revision > 0 AND policy_revision > 0 AND action = 'target.read' AND policy_id <> '00000000-0000-0000-0000-000000000000' AND ((scope_kind = 'environment' AND environment_id IS NOT NULL) OR (scope_kind = 'project_without_environment' AND environment_id IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_prj_policy_binding_history",
                schema: "app",
                table: "prj_policy_binding_history",
                sql: "binding_revision > 0 AND policy_revision > 0 AND octet_length(content_hash) = 32 AND action = 'target.read' AND ((scope_kind = 'environment' AND environment_id IS NOT NULL) OR (scope_kind = 'project_without_environment' AND environment_id IS NULL))");
        }
    }
}
