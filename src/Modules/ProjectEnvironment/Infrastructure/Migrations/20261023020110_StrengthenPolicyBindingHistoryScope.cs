using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kordra.Modules.ProjectEnvironment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StrengthenPolicyBindingHistoryScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_prj_policy_binding_history_prj_policy_bindings_binding_id_t~",
                schema: "app",
                table: "prj_policy_binding_history");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_prj_policy_bindings_binding_id_tenant_id_project_id_scope_k~",
                schema: "app",
                table: "prj_policy_bindings");

            migrationBuilder.DropIndex(
                name: "IX_prj_policy_binding_history_binding_id_tenant_id_project_id_~",
                schema: "app",
                table: "prj_policy_binding_history");

            migrationBuilder.AddColumn<Guid>(
                name: "scope_id",
                schema: "app",
                table: "prj_policy_bindings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "scope_id",
                schema: "app",
                table: "prj_policy_binding_history",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Derived existing scope identity only; never policy or permission backfill.
            migrationBuilder.Sql("UPDATE app.prj_policy_bindings SET scope_id = COALESCE(environment_id, project_id); UPDATE app.prj_policy_binding_history SET scope_id = COALESCE(environment_id, project_id);");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_prj_policy_bindings_binding_id_tenant_id_project_id_scope_k~",
                schema: "app",
                table: "prj_policy_bindings",
                columns: new[] { "binding_id", "tenant_id", "project_id", "scope_kind", "scope_id", "action" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_prj_policy_binding_scope_identity",
                schema: "app",
                table: "prj_policy_bindings",
                sql: "scope_id = COALESCE(environment_id, project_id)");

            migrationBuilder.CreateIndex(
                name: "IX_prj_policy_binding_history_binding_id_tenant_id_project_id_~",
                schema: "app",
                table: "prj_policy_binding_history",
                columns: new[] { "binding_id", "tenant_id", "project_id", "scope_kind", "scope_id", "action" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_prj_policy_binding_history_scope_identity",
                schema: "app",
                table: "prj_policy_binding_history",
                sql: "scope_id = COALESCE(environment_id, project_id)");

            migrationBuilder.AddForeignKey(
                name: "FK_prj_policy_binding_history_prj_policy_bindings_binding_id_t~",
                schema: "app",
                table: "prj_policy_binding_history",
                columns: new[] { "binding_id", "tenant_id", "project_id", "scope_kind", "scope_id", "action" },
                principalSchema: "app",
                principalTable: "prj_policy_bindings",
                principalColumns: new[] { "binding_id", "tenant_id", "project_id", "scope_kind", "scope_id", "action" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Authority history is additive; use a reviewed forward correction.");
    }
}
