using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kordra.Modules.ReleaseManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialReleaseIntent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "rel_lineages",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rel_lineages", x => x.id);
                    table.UniqueConstraint("AK_rel_lineages_id_installation_id_tenant_id_project_id", x => new { x.id, x.installation_id, x.tenant_id, x.project_id });
                    table.CheckConstraint("ck_rel_lineage", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND installation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND project_id <> '00000000-0000-0000-0000-000000000000'::uuid AND isfinite(created_at_utc)");
                });

            migrationBuilder.CreateTable(
                name: "rel_revisions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    release_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_number = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    creator_principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    artifact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    spec_bytes = table.Column<byte[]>(type: "bytea", nullable: true),
                    spec_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    frozen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retired_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retired_reason_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rel_revisions", x => x.id);
                    table.UniqueConstraint("AK_rel_revisions_id_installation_id_tenant_id_project_id", x => new { x.id, x.installation_id, x.tenant_id, x.project_id });
                    table.CheckConstraint("ck_rel_revision_ids", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND installation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND project_id <> '00000000-0000-0000-0000-000000000000'::uuid AND release_id <> '00000000-0000-0000-0000-000000000000'::uuid AND creator_principal_id <> '00000000-0000-0000-0000-000000000000'::uuid AND application_id <> '00000000-0000-0000-0000-000000000000'::uuid AND environment_id <> '00000000-0000-0000-0000-000000000000'::uuid AND target_id <> '00000000-0000-0000-0000-000000000000'::uuid AND artifact_id <> '00000000-0000-0000-0000-000000000000'::uuid AND revision_number > 0 AND version > 0");
                    table.CheckConstraint("ck_rel_revision_state", "(state = 'Draft' AND spec_bytes IS NULL AND spec_hash IS NULL AND frozen_at_utc IS NULL AND retired_at_utc IS NULL AND retired_reason_code IS NULL) OR (state = 'Frozen' AND spec_bytes IS NOT NULL AND spec_hash IS NOT NULL AND octet_length(spec_hash) = 32 AND octet_length(spec_bytes) BETWEEN 1 AND 65536 AND frozen_at_utc IS NOT NULL AND retired_at_utc IS NULL AND retired_reason_code IS NULL) OR (state = 'Retired' AND spec_bytes IS NOT NULL AND spec_hash IS NOT NULL AND octet_length(spec_hash) = 32 AND octet_length(spec_bytes) BETWEEN 1 AND 65536 AND frozen_at_utc IS NOT NULL AND retired_at_utc IS NOT NULL AND retired_reason_code IN ('obsolete','operator_cancelled'))");
                    table.CheckConstraint("ck_rel_revision_times", "isfinite(created_at_utc) AND isfinite(updated_at_utc) AND (frozen_at_utc IS NULL OR isfinite(frozen_at_utc)) AND (retired_at_utc IS NULL OR isfinite(retired_at_utc))");
                    table.ForeignKey(
                        name: "FK_rel_revisions_rel_lineages_release_id_installation_id_tenan~",
                        columns: x => new { x.release_id, x.installation_id, x.tenant_id, x.project_id },
                        principalSchema: "app",
                        principalTable: "rel_lineages",
                        principalColumns: new[] { "id", "installation_id", "tenant_id", "project_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rel_commands",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    release_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scoped_key_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    business_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    audit_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audit_intent_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rel_commands", x => x.id);
                    table.CheckConstraint("ck_rel_command", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND installation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND project_id <> '00000000-0000-0000-0000-000000000000'::uuid AND principal_id <> '00000000-0000-0000-0000-000000000000'::uuid AND release_revision_id <> '00000000-0000-0000-0000-000000000000'::uuid AND audit_intent_id <> '00000000-0000-0000-0000-000000000000'::uuid AND octet_length(scoped_key_hash) = 32 AND octet_length(business_hash) = 32 AND octet_length(audit_intent_hash) = 32 AND isfinite(created_at_utc)");
                    table.ForeignKey(
                        name: "FK_rel_commands_rel_revisions_release_revision_id_installation~",
                        columns: x => new { x.release_revision_id, x.installation_id, x.tenant_id, x.project_id },
                        principalSchema: "app",
                        principalTable: "rel_revisions",
                        principalColumns: new[] { "id", "installation_id", "tenant_id", "project_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_rel_commands_installation_id_tenant_id_principal_id_action_~",
                schema: "app",
                table: "rel_commands",
                columns: new[] { "installation_id", "tenant_id", "principal_id", "action", "scoped_key_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rel_commands_release_revision_id_installation_id_tenant_id_~",
                schema: "app",
                table: "rel_commands",
                columns: new[] { "release_revision_id", "installation_id", "tenant_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_rel_revisions_release_id",
                schema: "app",
                table: "rel_revisions",
                column: "release_id",
                unique: true,
                filter: "state = 'Draft'");

            migrationBuilder.CreateIndex(
                name: "IX_rel_revisions_release_id_installation_id_tenant_id_project_~",
                schema: "app",
                table: "rel_revisions",
                columns: new[] { "release_id", "installation_id", "tenant_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_rel_revisions_release_id_revision_number",
                schema: "app",
                table: "rel_revisions",
                columns: new[] { "release_id", "revision_number" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION app.rel_reject_history_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'REL history is immutable'; END $$;
                CREATE TRIGGER rel_lineages_immutable BEFORE UPDATE OR DELETE ON app.rel_lineages
                    FOR EACH ROW EXECUTE FUNCTION app.rel_reject_history_change();
                CREATE TRIGGER rel_commands_immutable BEFORE UPDATE OR DELETE ON app.rel_commands
                    FOR EACH ROW EXECUTE FUNCTION app.rel_reject_history_change();
                CREATE TRIGGER rel_lineages_no_truncate BEFORE TRUNCATE ON app.rel_lineages
                    FOR EACH STATEMENT EXECUTE FUNCTION app.rel_reject_history_change();
                CREATE TRIGGER rel_revisions_no_truncate BEFORE TRUNCATE ON app.rel_revisions
                    FOR EACH STATEMENT EXECUTE FUNCTION app.rel_reject_history_change();
                CREATE TRIGGER rel_commands_no_truncate BEFORE TRUNCATE ON app.rel_commands
                    FOR EACH STATEMENT EXECUTE FUNCTION app.rel_reject_history_change();
                CREATE FUNCTION app.rel_guard_revision() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE previous_number bigint;
                BEGIN
                    IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'REL revisions cannot be deleted'; END IF;
                    IF TG_OP = 'INSERT' THEN
                        IF NEW.state <> 'Draft' OR NEW.version <> 1 THEN RAISE EXCEPTION 'REL revisions start as Draft at version one'; END IF;
                        SELECT COALESCE(MAX(revision_number), 0) INTO previous_number FROM app.rel_revisions WHERE release_id = NEW.release_id;
                        IF NEW.revision_number <> previous_number + 1 THEN RAISE EXCEPTION 'REL revision numbers must advance'; END IF;
                        IF previous_number > 0 AND NOT EXISTS (SELECT 1 FROM app.rel_revisions WHERE release_id = NEW.release_id
                            AND revision_number = previous_number AND state IN ('Frozen','Retired'))
                            THEN RAISE EXCEPTION 'REL next revision requires a prior frozen or retired revision'; END IF;
                        RETURN NEW;
                    END IF;
                    IF NEW.id <> OLD.id OR NEW.installation_id <> OLD.installation_id OR NEW.tenant_id <> OLD.tenant_id OR
                        NEW.project_id <> OLD.project_id OR NEW.release_id <> OLD.release_id OR NEW.revision_number <> OLD.revision_number OR
                        NEW.creator_principal_id <> OLD.creator_principal_id OR NEW.created_at_utc <> OLD.created_at_utc OR
                        NEW.version <> OLD.version + 1 OR NEW.updated_at_utc < OLD.updated_at_utc
                        THEN RAISE EXCEPTION 'REL immutable identity or revision mismatch'; END IF;
                    IF OLD.state = 'Draft' AND NEW.state = 'Draft' THEN RETURN NEW; END IF;
                    IF OLD.state = 'Draft' AND NEW.state = 'Frozen' AND NEW.label = OLD.label AND
                        NEW.application_id = OLD.application_id AND NEW.environment_id = OLD.environment_id AND
                        NEW.target_id = OLD.target_id AND NEW.artifact_id = OLD.artifact_id THEN RETURN NEW; END IF;
                    IF OLD.state = 'Frozen' AND NEW.state = 'Retired' AND NEW.label = OLD.label AND
                        NEW.application_id = OLD.application_id AND NEW.environment_id = OLD.environment_id AND
                        NEW.target_id = OLD.target_id AND NEW.artifact_id = OLD.artifact_id AND
                        NEW.spec_bytes = OLD.spec_bytes AND NEW.spec_hash = OLD.spec_hash AND NEW.frozen_at_utc = OLD.frozen_at_utc
                        THEN RETURN NEW; END IF;
                    RAISE EXCEPTION 'REL state transition or frozen content is invalid';
                END $$;
                CREATE TRIGGER rel_revision_guard BEFORE INSERT OR UPDATE OR DELETE ON app.rel_revisions
                    FOR EACH ROW EXECUTE FUNCTION app.rel_guard_revision();
                REVOKE ALL ON app.rel_lineages, app.rel_revisions, app.rel_commands FROM PUBLIC;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'kordra_app') THEN
                        REVOKE ALL ON app.rel_lineages, app.rel_revisions, app.rel_commands FROM kordra_app;
                        GRANT SELECT, INSERT ON app.rel_lineages, app.rel_revisions, app.rel_commands TO kordra_app;
                        GRANT UPDATE (version, label, application_id, environment_id, target_id, artifact_id, state,
                            spec_bytes, spec_hash, updated_at_utc, frozen_at_utc, retired_at_utc, retired_reason_code)
                            ON app.rel_revisions TO kordra_app;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new InvalidOperationException("REL immutable release history cannot be removed by migration rollback.");
    }
}
