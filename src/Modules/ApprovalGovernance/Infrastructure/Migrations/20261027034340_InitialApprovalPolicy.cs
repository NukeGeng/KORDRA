using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kordra.Modules.ApprovalGovernance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialApprovalPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "apr_policy_heads",
                schema: "app",
                columns: table => new
                {
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apr_policy_heads", x => new { x.installation_id, x.tenant_id, x.project_id, x.policy_id });
                    table.CheckConstraint("ck_apr_policy_head", "installation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND project_id <> '00000000-0000-0000-0000-000000000000'::uuid AND policy_id <> '00000000-0000-0000-0000-000000000000'::uuid AND current_revision > 0");
                });

            migrationBuilder.CreateTable(
                name: "apr_policy_revisions",
                schema: "app",
                columns: table => new
                {
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    canonical_payload = table.Column<byte[]>(type: "bytea", nullable: false),
                    payload_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apr_policy_revisions", x => new { x.installation_id, x.tenant_id, x.project_id, x.policy_id, x.revision });
                    table.CheckConstraint("ck_apr_policy_revision", "installation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND project_id <> '00000000-0000-0000-0000-000000000000'::uuid AND policy_id <> '00000000-0000-0000-0000-000000000000'::uuid AND revision > 0 AND octet_length(payload_hash) = 32 AND octet_length(canonical_payload) BETWEEN 1 AND 65536 AND isfinite(published_at_utc)");
                    table.ForeignKey(
                        name: "FK_apr_policy_revisions_apr_policy_heads_installation_id_tenan~",
                        columns: x => new { x.installation_id, x.tenant_id, x.project_id, x.policy_id },
                        principalSchema: "app",
                        principalTable: "apr_policy_heads",
                        principalColumns: new[] { "installation_id", "tenant_id", "project_id", "policy_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "apr_policy_bindings",
                schema: "app",
                columns: table => new
                {
                    binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    binding_revision = table.Column<long>(type: "bigint", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apr_policy_bindings", x => x.binding_id);
                    table.UniqueConstraint("AK_apr_policy_bindings_binding_id_installation_id_tenant_id_pr~", x => new { x.binding_id, x.installation_id, x.tenant_id, x.project_id, x.environment_id });
                    table.CheckConstraint("ck_apr_policy_binding", "binding_id <> '00000000-0000-0000-0000-000000000000'::uuid AND installation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND project_id <> '00000000-0000-0000-0000-000000000000'::uuid AND environment_id <> '00000000-0000-0000-0000-000000000000'::uuid AND policy_id <> '00000000-0000-0000-0000-000000000000'::uuid AND binding_revision > 0 AND policy_revision > 0");
                    table.ForeignKey(
                        name: "FK_apr_policy_bindings_apr_policy_revisions_installation_id_te~",
                        columns: x => new { x.installation_id, x.tenant_id, x.project_id, x.policy_id, x.policy_revision },
                        principalSchema: "app",
                        principalTable: "apr_policy_revisions",
                        principalColumns: new[] { "installation_id", "tenant_id", "project_id", "policy_id", "revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "apr_policy_commands",
                schema: "app",
                columns: table => new
                {
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    manifest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    command_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    payload_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    audit_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audit_intent_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apr_policy_commands", x => new { x.installation_id, x.manifest_id });
                    table.CheckConstraint("ck_apr_policy_command", "installation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND manifest_id <> '00000000-0000-0000-0000-000000000000'::uuid AND tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND project_id <> '00000000-0000-0000-0000-000000000000'::uuid AND policy_id <> '00000000-0000-0000-0000-000000000000'::uuid AND command_id <> '00000000-0000-0000-0000-000000000000'::uuid AND audit_intent_id <> '00000000-0000-0000-0000-000000000000'::uuid AND policy_revision > 0 AND octet_length(command_hash) = 32 AND octet_length(payload_hash) = 32 AND octet_length(audit_intent_hash) = 32 AND isfinite(created_at_utc)");
                    table.ForeignKey(
                        name: "FK_apr_policy_commands_apr_policy_revisions_installation_id_te~",
                        columns: x => new { x.installation_id, x.tenant_id, x.project_id, x.policy_id, x.policy_revision },
                        principalSchema: "app",
                        principalTable: "apr_policy_revisions",
                        principalColumns: new[] { "installation_id", "tenant_id", "project_id", "policy_id", "revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "apr_policy_binding_history",
                schema: "app",
                columns: table => new
                {
                    binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    binding_revision = table.Column<long>(type: "bigint", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    manifest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bound_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apr_policy_binding_history", x => new { x.binding_id, x.binding_revision });
                    table.CheckConstraint("ck_apr_binding_history", "binding_id <> '00000000-0000-0000-0000-000000000000'::uuid AND installation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND project_id <> '00000000-0000-0000-0000-000000000000'::uuid AND environment_id <> '00000000-0000-0000-0000-000000000000'::uuid AND policy_id <> '00000000-0000-0000-0000-000000000000'::uuid AND manifest_id <> '00000000-0000-0000-0000-000000000000'::uuid AND binding_revision > 0 AND policy_revision > 0 AND isfinite(bound_at_utc)");
                    table.ForeignKey(
                        name: "FK_apr_policy_binding_history_apr_policy_bindings_binding_id_i~",
                        columns: x => new { x.binding_id, x.installation_id, x.tenant_id, x.project_id, x.environment_id },
                        principalSchema: "app",
                        principalTable: "apr_policy_bindings",
                        principalColumns: new[] { "binding_id", "installation_id", "tenant_id", "project_id", "environment_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_apr_policy_binding_history_apr_policy_revisions_installatio~",
                        columns: x => new { x.installation_id, x.tenant_id, x.project_id, x.policy_id, x.policy_revision },
                        principalSchema: "app",
                        principalTable: "apr_policy_revisions",
                        principalColumns: new[] { "installation_id", "tenant_id", "project_id", "policy_id", "revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_apr_policy_binding_history_binding_id_installation_id_tenan~",
                schema: "app",
                table: "apr_policy_binding_history",
                columns: new[] { "binding_id", "installation_id", "tenant_id", "project_id", "environment_id" });

            migrationBuilder.CreateIndex(
                name: "IX_apr_policy_binding_history_installation_id_tenant_id_projec~",
                schema: "app",
                table: "apr_policy_binding_history",
                columns: new[] { "installation_id", "tenant_id", "project_id", "policy_id", "policy_revision" });

            migrationBuilder.CreateIndex(
                name: "IX_apr_policy_bindings_installation_id_tenant_id_project_id_en~",
                schema: "app",
                table: "apr_policy_bindings",
                columns: new[] { "installation_id", "tenant_id", "project_id", "environment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_apr_policy_bindings_installation_id_tenant_id_project_id_po~",
                schema: "app",
                table: "apr_policy_bindings",
                columns: new[] { "installation_id", "tenant_id", "project_id", "policy_id", "policy_revision" });

            migrationBuilder.CreateIndex(
                name: "IX_apr_policy_commands_command_id",
                schema: "app",
                table: "apr_policy_commands",
                column: "command_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_apr_policy_commands_installation_id_tenant_id_project_id_po~",
                schema: "app",
                table: "apr_policy_commands",
                columns: new[] { "installation_id", "tenant_id", "project_id", "policy_id", "policy_revision" });
            migrationBuilder.Sql("""
                CREATE FUNCTION app.apr_reject_immutable_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'APR history is immutable'; END $$;
                CREATE TRIGGER apr_revisions_immutable BEFORE UPDATE OR DELETE ON app.apr_policy_revisions
                    FOR EACH ROW EXECUTE FUNCTION app.apr_reject_immutable_change();
                CREATE TRIGGER apr_binding_history_immutable BEFORE UPDATE OR DELETE ON app.apr_policy_binding_history
                    FOR EACH ROW EXECUTE FUNCTION app.apr_reject_immutable_change();
                CREATE TRIGGER apr_policy_commands_immutable BEFORE UPDATE OR DELETE ON app.apr_policy_commands
                    FOR EACH ROW EXECUTE FUNCTION app.apr_reject_immutable_change();
                CREATE TRIGGER apr_revisions_no_truncate BEFORE TRUNCATE ON app.apr_policy_revisions
                    FOR EACH STATEMENT EXECUTE FUNCTION app.apr_reject_immutable_change();
                CREATE TRIGGER apr_binding_history_no_truncate BEFORE TRUNCATE ON app.apr_policy_binding_history
                    FOR EACH STATEMENT EXECUTE FUNCTION app.apr_reject_immutable_change();
                CREATE TRIGGER apr_commands_no_truncate BEFORE TRUNCATE ON app.apr_policy_commands
                    FOR EACH STATEMENT EXECUTE FUNCTION app.apr_reject_immutable_change();
                CREATE TRIGGER apr_heads_no_truncate BEFORE TRUNCATE ON app.apr_policy_heads
                    FOR EACH STATEMENT EXECUTE FUNCTION app.apr_reject_immutable_change();
                CREATE TRIGGER apr_bindings_no_truncate BEFORE TRUNCATE ON app.apr_policy_bindings
                    FOR EACH STATEMENT EXECUTE FUNCTION app.apr_reject_immutable_change();
                CREATE FUNCTION app.apr_guard_policy_head() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'APR policy head cannot be deleted'; END IF;
                    IF TG_OP = 'INSERT' AND NEW.current_revision <> 1 THEN RAISE EXCEPTION 'APR initial policy revision must be one'; END IF;
                    IF TG_OP = 'UPDATE' AND (NEW.installation_id <> OLD.installation_id OR NEW.tenant_id <> OLD.tenant_id OR
                        NEW.project_id <> OLD.project_id OR NEW.policy_id <> OLD.policy_id OR
                        NEW.current_revision <> OLD.current_revision + 1) THEN RAISE EXCEPTION 'APR policy revision must advance exactly once'; END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER apr_policy_head_guard BEFORE INSERT OR UPDATE OR DELETE ON app.apr_policy_heads
                    FOR EACH ROW EXECUTE FUNCTION app.apr_guard_policy_head();
                CREATE FUNCTION app.apr_guard_policy_binding() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'APR binding cannot be deleted'; END IF;
                    IF TG_OP = 'INSERT' AND NEW.binding_revision <> 1 THEN RAISE EXCEPTION 'APR initial binding revision must be one'; END IF;
                    IF TG_OP = 'UPDATE' AND (NEW.binding_id <> OLD.binding_id OR NEW.installation_id <> OLD.installation_id OR
                        NEW.tenant_id <> OLD.tenant_id OR NEW.project_id <> OLD.project_id OR NEW.environment_id <> OLD.environment_id OR
                        NEW.binding_revision <> OLD.binding_revision + 1) THEN RAISE EXCEPTION 'APR binding identity and revision are immutable'; END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER apr_policy_binding_guard BEFORE INSERT OR UPDATE OR DELETE ON app.apr_policy_bindings
                    FOR EACH ROW EXECUTE FUNCTION app.apr_guard_policy_binding();
                REVOKE ALL ON app.apr_policy_heads, app.apr_policy_revisions, app.apr_policy_bindings, app.apr_policy_binding_history, app.apr_policy_commands FROM PUBLIC;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'kordra_app') THEN
                        REVOKE ALL ON app.apr_policy_heads, app.apr_policy_revisions, app.apr_policy_bindings, app.apr_policy_binding_history, app.apr_policy_commands FROM kordra_app;
                        GRANT SELECT ON app.apr_policy_heads, app.apr_policy_revisions, app.apr_policy_bindings TO kordra_app;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new InvalidOperationException("APR immutable policy history cannot be removed by migration rollback.");
    }
}
