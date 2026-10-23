using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kordra.Modules.IdentityAccess.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddResourcePolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "iam_resource_policies",
                schema: "app",
                columns: table => new
                {
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_iam_resource_policies", x => x.policy_id);
                    table.UniqueConstraint("AK_iam_resource_policies_tenant_id_project_id_policy_id", x => new { x.tenant_id, x.project_id, x.policy_id });
                    table.CheckConstraint("ck_iam_resource_policy_identity", "policy_id <> '00000000-0000-0000-0000-000000000000' AND tenant_id <> '00000000-0000-0000-0000-000000000000' AND project_id <> '00000000-0000-0000-0000-000000000000'");
                });

            migrationBuilder.CreateTable(
                name: "iam_resource_policy_commands",
                schema: "app",
                columns: table => new
                {
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    manifest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    result_json = table.Column<string>(type: "jsonb", nullable: false),
                    audit_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audit_intent_hash = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_iam_resource_policy_commands", x => new { x.installation_id, x.manifest_id });
                    table.CheckConstraint("ck_iam_resource_policy_receipt", "octet_length(content_hash) = 32 AND length(audit_intent_hash) = 64");
                });

            migrationBuilder.CreateTable(
                name: "iam_resource_policy_revisions",
                schema: "app",
                columns: table => new
                {
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scope_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    effective_from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    content_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_iam_resource_policy_revisions", x => new { x.policy_id, x.revision });
                    table.CheckConstraint("ck_iam_resource_policy_payload", "revision > 0 AND schema_version = 1 AND action = 'target.read' AND scope_kind IN ('environment','project_without_environment') AND status IN ('active','inactive') AND octet_length(content_hash) = 32 AND isfinite(effective_from_utc) AND isfinite(published_at_utc) AND (expires_at_utc IS NULL OR (isfinite(expires_at_utc) AND expires_at_utc > effective_from_utc))");
                    table.ForeignKey(
                        name: "FK_iam_resource_policy_revisions_iam_resource_policies_policy_~",
                        column: x => x.policy_id,
                        principalSchema: "app",
                        principalTable: "iam_resource_policies",
                        principalColumn: "policy_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "iam_resource_policy_heads",
                schema: "app",
                columns: table => new
                {
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_iam_resource_policy_heads", x => x.policy_id);
                    table.CheckConstraint("ck_iam_resource_policy_head", "current_revision > 0");
                    table.ForeignKey(
                        name: "FK_iam_resource_policy_heads_iam_resource_policy_revisions_pol~",
                        columns: x => new { x.policy_id, x.current_revision },
                        principalSchema: "app",
                        principalTable: "iam_resource_policy_revisions",
                        principalColumns: new[] { "policy_id", "revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "iam_resource_policy_rules",
                schema: "app",
                columns: table => new
                {
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    rule_case = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    effect = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_iam_resource_policy_rules", x => new { x.policy_id, x.revision, x.rule_case, x.effect });
                    table.CheckConstraint("ck_iam_resource_policy_rule", "revision > 0 AND rule_case IN ('Development','Test','Staging','Production','NoEnvironment') AND effect IN ('allow','deny')");
                    table.ForeignKey(
                        name: "FK_iam_resource_policy_rules_iam_resource_policy_revisions_pol~",
                        columns: x => new { x.policy_id, x.revision },
                        principalSchema: "app",
                        principalTable: "iam_resource_policy_revisions",
                        principalColumns: new[] { "policy_id", "revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_iam_resource_policy_heads_policy_id_current_revision",
                schema: "app",
                table: "iam_resource_policy_heads",
                columns: new[] { "policy_id", "current_revision" });
            migrationBuilder.Sql("""
                DO $grants$ BEGIN
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'kordra_app') THEN
                REVOKE ALL ON TABLE app.iam_resource_policies FROM PUBLIC; REVOKE ALL ON TABLE app.iam_resource_policies FROM kordra_app; GRANT SELECT ON TABLE app.iam_resource_policies TO kordra_app;
                REVOKE ALL ON TABLE app.iam_resource_policy_revisions FROM PUBLIC; REVOKE ALL ON TABLE app.iam_resource_policy_revisions FROM kordra_app; GRANT SELECT ON TABLE app.iam_resource_policy_revisions TO kordra_app;
                REVOKE ALL ON TABLE app.iam_resource_policy_rules FROM PUBLIC; REVOKE ALL ON TABLE app.iam_resource_policy_rules FROM kordra_app; GRANT SELECT ON TABLE app.iam_resource_policy_rules TO kordra_app;
                REVOKE ALL ON TABLE app.iam_resource_policy_heads FROM PUBLIC; REVOKE ALL ON TABLE app.iam_resource_policy_heads FROM kordra_app; GRANT SELECT ON TABLE app.iam_resource_policy_heads TO kordra_app;
                REVOKE ALL ON TABLE app.iam_resource_policy_commands FROM PUBLIC; REVOKE ALL ON TABLE app.iam_resource_policy_commands FROM kordra_app; GRANT SELECT ON TABLE app.iam_resource_policy_commands TO kordra_app;
                END IF;
                END $grants$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Authority history and clock floors are additive; use a reviewed forward correction.");
    }
}
