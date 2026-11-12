using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kordra.Modules.IdentityAccess.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowPolicyActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_iam_resource_policy_payload",
                schema: "app",
                table: "iam_resource_policy_revisions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_iam_resource_policy_payload",
                schema: "app",
                table: "iam_resource_policy_revisions",
                sql: "revision > 0 AND ((schema_version = 1 AND action = 'target.read' AND scope_kind IN ('environment','project_without_environment')) OR (schema_version = 2 AND action IN ('workflow.start','workflow.read','workflow.cancel','workflow.reconcile') AND scope_kind = 'environment')) AND status IN ('active','inactive') AND octet_length(content_hash) = 32 AND isfinite(effective_from_utc) AND isfinite(published_at_utc) AND (expires_at_utc IS NULL OR (isfinite(expires_at_utc) AND expires_at_utc > effective_from_utc))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_iam_resource_policy_payload",
                schema: "app",
                table: "iam_resource_policy_revisions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_iam_resource_policy_payload",
                schema: "app",
                table: "iam_resource_policy_revisions",
                sql: "revision > 0 AND schema_version = 1 AND action = 'target.read' AND scope_kind IN ('environment','project_without_environment') AND status IN ('active','inactive') AND octet_length(content_hash) = 32 AND isfinite(effective_from_utc) AND isfinite(published_at_utc) AND (expires_at_utc IS NULL OR (isfinite(expires_at_utc) AND expires_at_utc > effective_from_utc))");
        }
    }
}
