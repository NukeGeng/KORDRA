using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kordra.Modules.ArtifactManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialArtifactRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "art_artifacts",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repository = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    root_digest = table.Column<string>(type: "character varying(135)", maxLength: 135, nullable: false),
                    platform_json = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    root_media_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    root_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    selected_manifest_digest = table.Column<string>(type: "character varying(135)", maxLength: 135, nullable: false),
                    selected_manifest_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    selected_manifest_media_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    config_digest = table.Column<string>(type: "character varying(135)", maxLength: 135, nullable: false),
                    config_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    observed_platform_json = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    variant_evidence = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    verifier_profile = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    verified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_art_artifacts", x => x.id);
                    table.UniqueConstraint("AK_art_artifacts_id_tenant_id_project_id", x => new { x.id, x.tenant_id, x.project_id });
                    table.CheckConstraint("ck_art_digest", "(root_digest COLLATE \"C\" ~ '^sha256:[0-9a-f]{64}$' OR root_digest COLLATE \"C\" ~ '^sha512:[0-9a-f]{128}$') AND (selected_manifest_digest COLLATE \"C\" ~ '^sha256:[0-9a-f]{64}$' OR selected_manifest_digest COLLATE \"C\" ~ '^sha512:[0-9a-f]{128}$') AND (config_digest COLLATE \"C\" ~ '^sha256:[0-9a-f]{64}$' OR config_digest COLLATE \"C\" ~ '^sha512:[0-9a-f]{128}$')");
                    table.CheckConstraint("ck_art_ids", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND project_id <> '00000000-0000-0000-0000-000000000000'::uuid AND registry_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_art_media_type", "selected_manifest_media_type IN ('application/vnd.oci.image.manifest.v1+json','application/vnd.docker.distribution.manifest.v2+json') AND (root_media_type IN ('application/vnd.oci.image.index.v1+json','application/vnd.docker.distribution.manifest.list.v2+json') OR (root_media_type = selected_manifest_media_type AND root_digest = selected_manifest_digest AND root_size_bytes = selected_manifest_size_bytes))");
                    table.CheckConstraint("ck_art_platform", "(variant_evidence = 'none' AND ((platform_json = '{\"os\":\"linux\",\"architecture\":\"amd64\",\"variant\":null}' AND observed_platform_json = '{\"os\":\"linux\",\"architecture\":\"amd64\",\"variant\":null}') OR (platform_json = '{\"os\":\"linux\",\"architecture\":\"arm64\",\"variant\":null}' AND observed_platform_json = '{\"os\":\"linux\",\"architecture\":\"arm64\",\"variant\":null}'))) OR (variant_evidence = 'config' AND platform_json = '{\"os\":\"linux\",\"architecture\":\"arm64\",\"variant\":\"v8\"}' AND observed_platform_json = '{\"os\":\"linux\",\"architecture\":\"arm64\",\"variant\":\"v8\"}') OR (variant_evidence = 'descriptor' AND platform_json = '{\"os\":\"linux\",\"architecture\":\"arm64\",\"variant\":\"v8\"}' AND observed_platform_json = '{\"os\":\"linux\",\"architecture\":\"arm64\",\"variant\":null}' AND root_media_type IN ('application/vnd.oci.image.index.v1+json','application/vnd.docker.distribution.manifest.list.v2+json'))");
                    table.CheckConstraint("ck_art_profile", "verifier_profile = 'artifact-registry-v1' AND variant_evidence IN ('none','config','descriptor')");
                    table.CheckConstraint("ck_art_repository", "repository COLLATE \"C\" ~ '^[a-z0-9]+((\\.|_|__|-+)[a-z0-9]+)*(/[a-z0-9]+((\\.|_|__|-+)[a-z0-9]+)*)*$' AND repository COLLATE \"C\" !~ '[^a-z0-9._/-]'");
                    table.CheckConstraint("ck_art_sizes", "root_size_bytes BETWEEN 1 AND 1048576 AND selected_manifest_size_bytes BETWEEN 1 AND 1048576 AND config_size_bytes BETWEEN 1 AND 1048576");
                    table.CheckConstraint("ck_art_times", "isfinite(verified_at_utc) AND isfinite(created_at_utc) AND verified_at_utc <= created_at_utc");
                });

            migrationBuilder.CreateTable(
                name: "art_registration_receipts",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    business_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    identity_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    request_json = table.Column<string>(type: "jsonb", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    artifact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_verified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    safe_reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_art_registration_receipts", x => x.id);
                    table.UniqueConstraint("AK_art_registration_receipts_id_tenant_id_project_id", x => new { x.id, x.tenant_id, x.project_id });
                    table.CheckConstraint("ck_art_registration_hashes", "key_hash COLLATE \"C\" ~ '^[0-9a-f]{64}$' AND business_hash COLLATE \"C\" ~ '^[0-9a-f]{64}$' AND identity_hash COLLATE \"C\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_art_registration_ids", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND installation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND project_id <> '00000000-0000-0000-0000-000000000000'::uuid AND principal_id <> '00000000-0000-0000-0000-000000000000'::uuid AND boot_id <> '00000000-0000-0000-0000-000000000000'::uuid AND invocation_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_art_registration_state", "(state = 'Pending' AND artifact_id IS NULL AND completed_at_utc IS NULL AND attempt_verified_at_utc IS NULL AND safe_reason IS NULL) OR (state = 'Registered' AND artifact_id IS NOT NULL AND completed_at_utc IS NOT NULL AND attempt_verified_at_utc IS NOT NULL AND safe_reason IS NULL) OR (state IN ('Failed','Abandoned') AND artifact_id IS NULL AND completed_at_utc IS NOT NULL AND attempt_verified_at_utc IS NULL AND safe_reason IS NOT NULL)");
                    table.CheckConstraint("ck_art_registration_times", "isfinite(created_at_utc) AND (completed_at_utc IS NULL OR (isfinite(completed_at_utc) AND completed_at_utc >= created_at_utc)) AND (attempt_verified_at_utc IS NULL OR (isfinite(attempt_verified_at_utc) AND attempt_verified_at_utc >= created_at_utc AND attempt_verified_at_utc <= completed_at_utc))");
                    table.CheckConstraint("ck_art_request_json", "jsonb_typeof(request_json) = 'object' AND octet_length(request_json::text) <= 4096");
                    table.CheckConstraint("ck_art_safe_reason", "safe_reason IS NULL OR safe_reason ~ '^[a-z_]{1,64}$'");
                    table.ForeignKey(
                        name: "FK_art_registration_receipts_art_artifacts_artifact_id_tenant_~",
                        columns: x => new { x.artifact_id, x.tenant_id, x.project_id },
                        principalSchema: "app",
                        principalTable: "art_artifacts",
                        principalColumns: new[] { "id", "tenant_id", "project_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "art_provenance_claims",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    artifact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    declaration_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_art_provenance_claims", x => x.id);
                    table.CheckConstraint("ck_art_claim_ids", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid AND project_id <> '00000000-0000-0000-0000-000000000000'::uuid AND artifact_id <> '00000000-0000-0000-0000-000000000000'::uuid AND registration_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_art_claim_json", "jsonb_typeof(declaration_json) = 'object' AND octet_length(declaration_json::text) <= 1024");
                    table.CheckConstraint("ck_art_claim_time", "isfinite(created_at_utc)");
                    table.ForeignKey(
                        name: "FK_art_provenance_claims_art_artifacts_artifact_id_tenant_id_p~",
                        columns: x => new { x.artifact_id, x.tenant_id, x.project_id },
                        principalSchema: "app",
                        principalTable: "art_artifacts",
                        principalColumns: new[] { "id", "tenant_id", "project_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_art_provenance_claims_art_registration_receipts_registratio~",
                        columns: x => new { x.registration_id, x.tenant_id, x.project_id },
                        principalSchema: "app",
                        principalTable: "art_registration_receipts",
                        principalColumns: new[] { "id", "tenant_id", "project_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_art_artifacts_tenant_id_project_id_created_at_utc_id",
                schema: "app",
                table: "art_artifacts",
                columns: new[] { "tenant_id", "project_id", "created_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "uq_art_content_identity",
                schema: "app",
                table: "art_artifacts",
                columns: new[] { "tenant_id", "project_id", "registry_id", "repository", "root_digest", "platform_json" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_art_provenance_claims_artifact_id_tenant_id_project_id",
                schema: "app",
                table: "art_provenance_claims",
                columns: new[] { "artifact_id", "tenant_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_art_provenance_claims_registration_id",
                schema: "app",
                table: "art_provenance_claims",
                column: "registration_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_art_provenance_claims_registration_id_tenant_id_project_id",
                schema: "app",
                table: "art_provenance_claims",
                columns: new[] { "registration_id", "tenant_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_art_registration_receipts_artifact_id_tenant_id_project_id",
                schema: "app",
                table: "art_registration_receipts",
                columns: new[] { "artifact_id", "tenant_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "uq_art_registration_artifact_scope",
                schema: "app",
                table: "art_registration_receipts",
                columns: new[] { "id", "tenant_id", "project_id", "artifact_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_art_registration_key",
                schema: "app",
                table: "art_registration_receipts",
                columns: new[] { "installation_id", "tenant_id", "project_id", "principal_id", "key_hash" },
                unique: true);

            // ArtifactId is nullable while Pending, so EF's required alternate-key model cannot
            // express this fourth FK column. Defer this exact scope FK until all M writes have
            // been staged; the enclosing owner/AUD transaction must satisfy it before commit.
            migrationBuilder.Sql("""
                ALTER TABLE app.art_provenance_claims
                  ADD CONSTRAINT fk_art_claim_registration_artifact
                  FOREIGN KEY (registration_id, tenant_id, project_id, artifact_id)
                  REFERENCES app.art_registration_receipts (id, tenant_id, project_id, artifact_id)
                  DEFERRABLE INITIALLY DEFERRED;

                CREATE FUNCTION app.art_reject_immutable_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION USING ERRCODE = '55000', MESSAGE = 'ART immutable record cannot be changed';
                END $$;
                CREATE TRIGGER art_artifacts_immutable BEFORE UPDATE OR DELETE ON app.art_artifacts
                  FOR EACH ROW EXECUTE FUNCTION app.art_reject_immutable_change();
                CREATE TRIGGER art_claims_immutable BEFORE UPDATE OR DELETE ON app.art_provenance_claims
                  FOR EACH ROW EXECUTE FUNCTION app.art_reject_immutable_change();
                CREATE TRIGGER art_artifacts_no_truncate BEFORE TRUNCATE ON app.art_artifacts
                  FOR EACH STATEMENT EXECUTE FUNCTION app.art_reject_immutable_change();
                CREATE TRIGGER art_claims_no_truncate BEFORE TRUNCATE ON app.art_provenance_claims
                  FOR EACH STATEMENT EXECUTE FUNCTION app.art_reject_immutable_change();
                CREATE TRIGGER art_receipts_no_truncate BEFORE TRUNCATE ON app.art_registration_receipts
                  FOR EACH STATEMENT EXECUTE FUNCTION app.art_reject_immutable_change();

                CREATE FUNCTION app.art_guard_receipt_transition() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP <> 'UPDATE' THEN
                    RAISE EXCEPTION USING ERRCODE = '55000', MESSAGE = 'ART registration receipt cannot be deleted';
                  END IF;
                  IF OLD.state <> 'Pending' OR NEW.state NOT IN ('Registered','Failed','Abandoned') THEN
                    RAISE EXCEPTION USING ERRCODE = '55000', MESSAGE = 'ART registration terminal state cannot be rewritten';
                  END IF;
                  IF (to_jsonb(NEW) - ARRAY['state','artifact_id','completed_at_utc','attempt_verified_at_utc','safe_reason'])
                    IS DISTINCT FROM
                     (to_jsonb(OLD) - ARRAY['state','artifact_id','completed_at_utc','attempt_verified_at_utc','safe_reason']) THEN
                    RAISE EXCEPTION USING ERRCODE = '55000', MESSAGE = 'ART registration business identity is immutable';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER art_receipt_transition BEFORE UPDATE OR DELETE ON app.art_registration_receipts
                  FOR EACH ROW EXECUTE FUNCTION app.art_guard_receipt_transition();

                REVOKE ALL ON app.art_artifacts, app.art_registration_receipts, app.art_provenance_claims FROM PUBLIC;
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'kordra_app') THEN
                    REVOKE UPDATE, DELETE, TRUNCATE ON app.art_artifacts, app.art_provenance_claims FROM kordra_app;
                    REVOKE UPDATE, DELETE, TRUNCATE ON app.art_registration_receipts FROM kordra_app;
                    GRANT SELECT, INSERT ON app.art_artifacts, app.art_provenance_claims, app.art_registration_receipts TO kordra_app;
                    GRANT UPDATE (state, artifact_id, completed_at_utc, attempt_verified_at_utc, safe_reason)
                      ON app.art_registration_receipts TO kordra_app;
                  END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("ART catalog and registration history cannot be removed by downgrade. Disable registration and retain the schema/history.");
    }
}
