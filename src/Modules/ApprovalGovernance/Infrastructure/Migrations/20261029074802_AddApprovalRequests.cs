using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kordra.Modules.ApprovalGovernance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "apr_requests",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    release_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requester_principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    spec_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    requester_evidence = table.Column<byte[]>(type: "bytea", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    pending_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    granted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    grant_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approver_principal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approver_evidence = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apr_requests", x => x.id);
                    table.UniqueConstraint("AK_apr_requests_id_installation_id_tenant_id", x => new { x.id, x.installation_id, x.tenant_id });
                    table.CheckConstraint("ck_apr_request", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND installation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND project_id <> '00000000-0000-0000-0000-000000000000'::uuid AND environment_id <> '00000000-0000-0000-0000-000000000000'::uuid AND release_revision_id <> '00000000-0000-0000-0000-000000000000'::uuid AND requester_principal_id <> '00000000-0000-0000-0000-000000000000'::uuid AND version > 0 AND state IN ('Pending','Granted','Rejected','Revoked') AND octet_length(spec_hash) = 32 AND octet_length(requester_evidence) BETWEEN 1 AND 65536 AND isfinite(created_at_utc) AND isfinite(pending_expires_at_utc) AND pending_expires_at_utc > created_at_utc AND (grant_expires_at_utc IS NULL OR (isfinite(grant_expires_at_utc) AND grant_expires_at_utc <= pending_expires_at_utc))");
                });

            migrationBuilder.CreateTable(
                name: "apr_decisions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_version = table.Column<long>(type: "bigint", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    actor_principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason_code = table.Column<string>(type: "text", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apr_decisions", x => x.id);
                    table.CheckConstraint("ck_apr_decision", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND installation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND actor_principal_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_version > 1 AND isfinite(occurred_at_utc) AND ((action = 'grant' AND reason_code = 'approved') OR (action = 'reject' AND reason_code IN ('changes_required','incorrect_artifact','incorrect_target','policy_denied')) OR (action = 'revoke' AND reason_code IN ('obsolete','requester_cancelled','owner_cancelled')))");
                    table.ForeignKey(
                        name: "FK_apr_decisions_apr_requests_request_id_installation_id_tenan~",
                        columns: x => new { x.request_id, x.installation_id, x.tenant_id },
                        principalSchema: "app",
                        principalTable: "apr_requests",
                        principalColumns: new[] { "id", "installation_id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "apr_request_commands",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    scoped_key_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    business_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    audit_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audit_intent_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    boot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apr_request_commands", x => x.id);
                    table.CheckConstraint("ck_apr_request_command", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND installation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND principal_id <> '00000000-0000-0000-0000-000000000000'::uuid AND request_id <> '00000000-0000-0000-0000-000000000000'::uuid AND audit_intent_id <> '00000000-0000-0000-0000-000000000000'::uuid AND boot_id <> '00000000-0000-0000-0000-000000000000'::uuid AND invocation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND action IN ('request','grant','reject','revoke') AND octet_length(scoped_key_hash) = 32 AND octet_length(business_hash) = 32 AND octet_length(audit_intent_hash) = 32 AND isfinite(created_at_utc)");
                    table.ForeignKey(
                        name: "FK_apr_request_commands_apr_requests_request_id_installation_i~",
                        columns: x => new { x.request_id, x.installation_id, x.tenant_id },
                        principalSchema: "app",
                        principalTable: "apr_requests",
                        principalColumns: new[] { "id", "installation_id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_apr_decisions_request_id_installation_id_tenant_id",
                schema: "app",
                table: "apr_decisions",
                columns: new[] { "request_id", "installation_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "IX_apr_decisions_request_id_request_version",
                schema: "app",
                table: "apr_decisions",
                columns: new[] { "request_id", "request_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_apr_request_commands_installation_id_tenant_id_principal_id~",
                schema: "app",
                table: "apr_request_commands",
                columns: new[] { "installation_id", "tenant_id", "principal_id", "action", "scoped_key_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_apr_request_commands_request_id_installation_id_tenant_id",
                schema: "app",
                table: "apr_request_commands",
                columns: new[] { "request_id", "installation_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "IX_apr_requests_installation_id_release_revision_id",
                schema: "app",
                table: "apr_requests",
                columns: new[] { "installation_id", "release_revision_id" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION app.apr_reject_request_history_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'approval history is immutable'; END $$;
                CREATE TRIGGER apr_decisions_immutable BEFORE UPDATE OR DELETE ON app.apr_decisions FOR EACH ROW EXECUTE FUNCTION app.apr_reject_request_history_change();
                CREATE TRIGGER apr_request_commands_immutable BEFORE UPDATE OR DELETE ON app.apr_request_commands FOR EACH ROW EXECUTE FUNCTION app.apr_reject_request_history_change();
                CREATE TRIGGER apr_decisions_no_truncate BEFORE TRUNCATE ON app.apr_decisions FOR EACH STATEMENT EXECUTE FUNCTION app.apr_reject_request_history_change();
                CREATE TRIGGER apr_request_commands_no_truncate BEFORE TRUNCATE ON app.apr_request_commands FOR EACH STATEMENT EXECUTE FUNCTION app.apr_reject_request_history_change();
                CREATE TRIGGER apr_requests_no_truncate BEFORE TRUNCATE ON app.apr_requests FOR EACH STATEMENT EXECUTE FUNCTION app.apr_reject_request_history_change();
                CREATE FUNCTION app.apr_guard_request() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'approval requests cannot be deleted'; END IF;
                  IF TG_OP = 'INSERT' THEN
                    IF NEW.state <> 'Pending' OR NEW.version <> 1 OR NEW.granted_at_utc IS NOT NULL OR NEW.grant_expires_at_utc IS NOT NULL OR NEW.approver_principal_id IS NOT NULL OR NEW.approver_evidence IS NOT NULL THEN
                      RAISE EXCEPTION 'approval request must start pending';
                    END IF;
                    RETURN NEW;
                  END IF;
                  IF (NEW.id,NEW.installation_id,NEW.tenant_id,NEW.project_id,NEW.environment_id,NEW.release_revision_id,NEW.requester_principal_id,NEW.spec_hash,NEW.requester_evidence,NEW.created_at_utc,NEW.pending_expires_at_utc)
                    IS DISTINCT FROM (OLD.id,OLD.installation_id,OLD.tenant_id,OLD.project_id,OLD.environment_id,OLD.release_revision_id,OLD.requester_principal_id,OLD.spec_hash,OLD.requester_evidence,OLD.created_at_utc,OLD.pending_expires_at_utc)
                    OR NEW.version <> OLD.version + 1 OR NOT ((OLD.state = 'Pending' AND NEW.state IN ('Granted','Rejected','Revoked')) OR (OLD.state = 'Granted' AND NEW.state = 'Revoked')) THEN
                    RAISE EXCEPTION 'invalid approval request transition';
                  END IF;
                  IF NEW.state = 'Granted' THEN
                    IF NEW.granted_at_utc IS NULL OR NEW.grant_expires_at_utc IS NULL OR NEW.approver_principal_id IS NULL OR NEW.approver_principal_id = '00000000-0000-0000-0000-000000000000'::uuid OR NEW.approver_evidence IS NULL OR octet_length(NEW.approver_evidence) NOT BETWEEN 1 AND 65536 OR NOT isfinite(NEW.granted_at_utc) OR NEW.granted_at_utc < NEW.created_at_utc OR NEW.grant_expires_at_utc <= NEW.granted_at_utc THEN
                      RAISE EXCEPTION 'invalid approval grant evidence';
                    END IF;
                  ELSIF (NEW.granted_at_utc,NEW.grant_expires_at_utc,NEW.approver_principal_id,NEW.approver_evidence) IS DISTINCT FROM (OLD.granted_at_utc,OLD.grant_expires_at_utc,OLD.approver_principal_id,OLD.approver_evidence) THEN
                    RAISE EXCEPTION 'grant evidence cannot be altered';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER apr_request_guard BEFORE INSERT OR UPDATE OR DELETE ON app.apr_requests FOR EACH ROW EXECUTE FUNCTION app.apr_guard_request();
                REVOKE ALL ON app.apr_requests, app.apr_decisions, app.apr_request_commands FROM PUBLIC;
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'kordra_app') THEN
                    REVOKE ALL ON app.apr_requests, app.apr_decisions, app.apr_request_commands FROM kordra_app;
                    GRANT SELECT, INSERT ON app.apr_requests, app.apr_decisions, app.apr_request_commands TO kordra_app;
                    GRANT UPDATE (version,state,granted_at_utc,grant_expires_at_utc,approver_principal_id,approver_evidence) ON app.apr_requests TO kordra_app;
                  END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new InvalidOperationException("Approval history cannot be removed by migration rollback.");
    }
}
