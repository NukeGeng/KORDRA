using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kordra.Modules.ConnectionManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RegistryCredentialAllocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sec_registry_command_receipts",
                schema: "app",
                columns: table => new
                {
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    manifest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    result_json = table.Column<string>(type: "jsonb", nullable: false),
                    audit_occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    audit_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audit_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sec_registry_command_receipts", x => new { x.installation_id, x.manifest_id });
                    table.CheckConstraint("ck_sec_registry_command_hashes", "content_hash COLLATE \"C\" ~ '^[0-9a-f]{64}$' AND audit_hash COLLATE \"C\" ~ '^[0-9a-f]{64}$' AND isfinite(audit_occurred_at_utc)");
                    table.CheckConstraint("ck_sec_registry_command_receipts_audit_intent_id", "audit_intent_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_registry_command_receipts_installation_id", "installation_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_registry_command_receipts_manifest_id", "manifest_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                });

            migrationBuilder.CreateTable(
                name: "sec_registry_profiles",
                schema: "app",
                columns: table => new
                {
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repository = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "C"),
                    consumer_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    operation = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider_kind = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    credential_format = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sec_registry_profiles", x => new { x.installation_id, x.tenant_id, x.profile_id });
                    table.UniqueConstraint("AK_sec_registry_profiles_installation_id_tenant_id_profile_id_~", x => new { x.installation_id, x.tenant_id, x.profile_id, x.project_id, x.registry_id, x.repository, x.consumer_id, x.operation });
                    table.CheckConstraint("ck_sec_registry_profile_plan", "consumer_id = 'artifact.registry.read.v1' AND operation = 'artifact.register' AND provider_kind = 'macos-keychain-file-v1' AND credential_format = 'opaque-bytes-v1'");
                    table.CheckConstraint("ck_sec_registry_profiles_installation_id", "installation_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_registry_profiles_profile_id", "profile_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_registry_profiles_project_id", "project_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_registry_profiles_registry_id", "registry_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_registry_profiles_tenant_id", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_registry_repository", "repository COLLATE \"C\" ~ '^[a-z0-9]+((\\.|_|__|-+)[a-z0-9]+)*(/[a-z0-9]+((\\.|_|__|-+)[a-z0-9]+)*)*$' AND repository COLLATE \"C\" !~ '[^a-z0-9._/-]'");
                });

            migrationBuilder.CreateTable(
                name: "sec_native_reference_allocations",
                schema: "app",
                columns: table => new
                {
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_kind = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    inventory_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    registry_profile_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sec_native_reference_allocations", x => new { x.installation_id, x.reference_id });
                    table.CheckConstraint("ck_sec_native_reference_allocations_installation_id", "installation_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_native_reference_allocations_reference_id", "reference_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_native_reference_allocations_tenant_id", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_reference_owner", "(owner_kind = 'inventory' AND inventory_profile_id IS NOT NULL AND registry_profile_id IS NULL) OR (owner_kind = 'registry' AND inventory_profile_id IS NULL AND registry_profile_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_sec_native_reference_allocations_sec_connection_profiles_in~",
                        columns: x => new { x.installation_id, x.tenant_id, x.inventory_profile_id },
                        principalSchema: "app",
                        principalTable: "sec_connection_profiles",
                        principalColumns: new[] { "installation_id", "tenant_id", "profile_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sec_native_reference_allocations_sec_registry_profiles_inst~",
                        columns: x => new { x.installation_id, x.tenant_id, x.registry_profile_id },
                        principalSchema: "app",
                        principalTable: "sec_registry_profiles",
                        principalColumns: new[] { "installation_id", "tenant_id", "profile_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sec_registry_references",
                schema: "app",
                columns: table => new
                {
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    locator_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sec_registry_references", x => new { x.installation_id, x.tenant_id, x.reference_id });
                    table.UniqueConstraint("AK_sec_registry_references_installation_id_tenant_id_profile_i~", x => new { x.installation_id, x.tenant_id, x.profile_id, x.reference_id });
                    table.CheckConstraint("ck_sec_registry_references_installation_id", "installation_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_registry_references_locator_id", "locator_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_registry_references_profile_id", "profile_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_registry_references_reference_id", "reference_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_registry_references_tenant_id", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_sec_registry_references_sec_native_reference_allocations_in~",
                        columns: x => new { x.installation_id, x.reference_id },
                        principalSchema: "app",
                        principalTable: "sec_native_reference_allocations",
                        principalColumns: new[] { "installation_id", "reference_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sec_registry_references_sec_registry_profiles_installation_~",
                        columns: x => new { x.installation_id, x.tenant_id, x.profile_id },
                        principalSchema: "app",
                        principalTable: "sec_registry_profiles",
                        principalColumns: new[] { "installation_id", "tenant_id", "profile_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sec_registry_binding_revisions",
                schema: "app",
                columns: table => new
                {
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repository = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "C"),
                    consumer_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    operation = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    not_before_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    manifest_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sec_registry_binding_revisions", x => new { x.installation_id, x.tenant_id, x.binding_id, x.revision });
                    table.UniqueConstraint("AK_sec_registry_binding_revisions_installation_id_tenant_id_pr~", x => new { x.installation_id, x.tenant_id, x.project_id, x.registry_id, x.repository, x.consumer_id, x.operation, x.binding_id, x.revision });
                    table.CheckConstraint("ck_sec_registry_binding_revisions_binding_id", "binding_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_registry_binding_revisions_installation_id", "installation_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_registry_binding_revisions_manifest_id", "manifest_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_registry_binding_revisions_profile_id", "profile_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_registry_binding_revisions_project_id", "project_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_registry_binding_revisions_reference_id", "reference_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_registry_binding_revisions_registry_id", "registry_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_registry_binding_revisions_tenant_id", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_registry_binding_state", "consumer_id = 'artifact.registry.read.v1' AND operation = 'artifact.register' AND revision > 0 AND state IN ('active','revoked') AND isfinite(not_before_utc) AND isfinite(expires_at_utc) AND not_before_utc < expires_at_utc");
                    table.ForeignKey(
                        name: "FK_sec_registry_binding_revisions_sec_registry_profiles_instal~",
                        columns: x => new { x.installation_id, x.tenant_id, x.profile_id, x.project_id, x.registry_id, x.repository, x.consumer_id, x.operation },
                        principalSchema: "app",
                        principalTable: "sec_registry_profiles",
                        principalColumns: new[] { "installation_id", "tenant_id", "profile_id", "project_id", "registry_id", "repository", "consumer_id", "operation" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sec_registry_binding_revisions_sec_registry_references_inst~",
                        columns: x => new { x.installation_id, x.tenant_id, x.profile_id, x.reference_id },
                        principalSchema: "app",
                        principalTable: "sec_registry_references",
                        principalColumns: new[] { "installation_id", "tenant_id", "profile_id", "reference_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sec_registry_binding_heads",
                schema: "app",
                columns: table => new
                {
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repository = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, collation: "C"),
                    consumer_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    operation = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sec_registry_binding_heads", x => new { x.installation_id, x.tenant_id, x.project_id, x.registry_id, x.repository, x.consumer_id, x.operation });
                    table.CheckConstraint("ck_sec_registry_binding_heads_binding_id", "binding_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_registry_binding_heads_installation_id", "installation_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_registry_binding_heads_project_id", "project_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_registry_binding_heads_registry_id", "registry_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_registry_binding_heads_tenant_id", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_registry_head", "consumer_id = 'artifact.registry.read.v1' AND operation = 'artifact.register' AND revision > 0");
                    table.ForeignKey(
                        name: "FK_sec_registry_binding_heads_sec_registry_binding_revisions_i~",
                        columns: x => new { x.installation_id, x.tenant_id, x.project_id, x.registry_id, x.repository, x.consumer_id, x.operation, x.binding_id, x.revision },
                        principalSchema: "app",
                        principalTable: "sec_registry_binding_revisions",
                        principalColumns: new[] { "installation_id", "tenant_id", "project_id", "registry_id", "repository", "consumer_id", "operation", "binding_id", "revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sec_native_reference_allocations_installation_id_tenant_id_~",
                schema: "app",
                table: "sec_native_reference_allocations",
                columns: new[] { "installation_id", "tenant_id", "inventory_profile_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sec_native_reference_allocations_installation_id_tenant_id~1",
                schema: "app",
                table: "sec_native_reference_allocations",
                columns: new[] { "installation_id", "tenant_id", "registry_profile_id" });

            migrationBuilder.CreateIndex(
                name: "uq_sec_inventory_reference_owner",
                schema: "app",
                table: "sec_native_reference_allocations",
                columns: new[] { "installation_id", "reference_id", "tenant_id", "inventory_profile_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_sec_registry_reference_owner",
                schema: "app",
                table: "sec_native_reference_allocations",
                columns: new[] { "installation_id", "reference_id", "tenant_id", "registry_profile_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sec_registry_binding_heads_installation_id_binding_id",
                schema: "app",
                table: "sec_registry_binding_heads",
                columns: new[] { "installation_id", "binding_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sec_registry_binding_heads_installation_id_tenant_id_projec~",
                schema: "app",
                table: "sec_registry_binding_heads",
                columns: new[] { "installation_id", "tenant_id", "project_id", "registry_id", "repository", "consumer_id", "operation", "binding_id", "revision" });

            migrationBuilder.CreateIndex(
                name: "IX_sec_registry_binding_revisions_installation_id_manifest_id",
                schema: "app",
                table: "sec_registry_binding_revisions",
                columns: new[] { "installation_id", "manifest_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sec_registry_binding_revisions_installation_id_tenant_id_p~1",
                schema: "app",
                table: "sec_registry_binding_revisions",
                columns: new[] { "installation_id", "tenant_id", "profile_id", "project_id", "registry_id", "repository", "consumer_id", "operation" });

            migrationBuilder.CreateIndex(
                name: "IX_sec_registry_binding_revisions_installation_id_tenant_id_pr~",
                schema: "app",
                table: "sec_registry_binding_revisions",
                columns: new[] { "installation_id", "tenant_id", "profile_id", "reference_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sec_registry_profiles_installation_id_profile_id",
                schema: "app",
                table: "sec_registry_profiles",
                columns: new[] { "installation_id", "profile_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sec_registry_references_installation_id_reference_id",
                schema: "app",
                table: "sec_registry_references",
                columns: new[] { "installation_id", "reference_id" },
                unique: true);

            // Backfill metadata only, before enforcing the existing reference table's new FK.
            // Conflicting identities fail this transaction; never inspect or infer Keychain data.
            migrationBuilder.Sql("""
                INSERT INTO app.sec_native_reference_allocations
                  (installation_id, reference_id, tenant_id, owner_kind, inventory_profile_id, registry_profile_id)
                SELECT installation_id, reference_id, tenant_id, 'inventory', profile_id, NULL
                FROM app.sec_secret_references;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_sec_secret_references_sec_native_reference_allocations_inst~",
                schema: "app",
                table: "sec_secret_references",
                columns: new[] { "installation_id", "reference_id" },
                principalSchema: "app",
                principalTable: "sec_native_reference_allocations",
                principalColumns: new[] { "installation_id", "reference_id" },
                onDelete: ReferentialAction.Restrict);
            // Nullable typed profile keys are unique indexes, not EF alternate keys.
            migrationBuilder.Sql("""
                ALTER TABLE app.sec_secret_references ADD CONSTRAINT fk_sec_inventory_allocation_owner
                  FOREIGN KEY (installation_id, reference_id, tenant_id, profile_id)
                  REFERENCES app.sec_native_reference_allocations (installation_id, reference_id, tenant_id, inventory_profile_id);
                ALTER TABLE app.sec_registry_references ADD CONSTRAINT fk_sec_registry_allocation_owner
                  FOREIGN KEY (installation_id, reference_id, tenant_id, profile_id)
                  REFERENCES app.sec_native_reference_allocations (installation_id, reference_id, tenant_id, registry_profile_id);
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'kordra_app') THEN
                    GRANT SELECT ON app.sec_native_reference_allocations, app.sec_registry_profiles,
                      app.sec_registry_references, app.sec_registry_binding_revisions, app.sec_registry_binding_heads TO kordra_app;
                    REVOKE INSERT, UPDATE, DELETE, TRUNCATE ON app.sec_native_reference_allocations, app.sec_registry_profiles,
                      app.sec_registry_references, app.sec_registry_binding_revisions, app.sec_registry_binding_heads FROM kordra_app;
                    REVOKE ALL ON app.sec_registry_command_receipts FROM kordra_app;
                  END IF;
                END $$;
                REVOKE ALL ON app.sec_native_reference_allocations, app.sec_registry_profiles, app.sec_registry_references,
                  app.sec_registry_binding_revisions, app.sec_registry_binding_heads, app.sec_registry_command_receipts FROM PUBLIC;
                """);
        }

        /// <summary>Retain global reference ownership even when application code is rolled back.</summary>
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Native reference allocations cannot be removed by downgrade. Retain schema/history and disable incompatible credential writers.");
    }
}
