using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kordra.Modules.ConnectionManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCredentialBoundary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "sec_command_receipts",
                schema: "app",
                columns: table => new
                {
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    manifest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    result_json = table.Column<string>(type: "jsonb", nullable: false),
                    audit_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audit_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sec_command_receipts", x => new { x.installation_id, x.manifest_id });
                    table.CheckConstraint("ck_sec_command_receipts_audit_intent_id", "audit_intent_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_command_receipts_installation_id", "installation_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_command_receipts_manifest_id", "manifest_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                });

            migrationBuilder.CreateTable(
                name: "sec_connection_profiles",
                schema: "app",
                columns: table => new
                {
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    operation = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider_kind = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    credential_format = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sec_connection_profiles", x => new { x.installation_id, x.tenant_id, x.profile_id });
                    table.UniqueConstraint("AK_sec_connection_profiles_installation_id_tenant_id_profile_i~", x => new { x.installation_id, x.tenant_id, x.profile_id, x.target_id, x.consumer_id, x.operation });
                    table.CheckConstraint("ck_sec_connection_profiles_installation_id", "installation_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_connection_profiles_profile_id", "profile_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_connection_profiles_target_id", "target_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_connection_profiles_tenant_id", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_profile_plan", "consumer_id = 'inventory.readonly.v1' AND operation = 'target.read' AND provider_kind = 'macos-keychain-file-v1' AND credential_format = 'opaque-bytes-v1'");
                });

            migrationBuilder.CreateTable(
                name: "sec_secret_references",
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
                    table.PrimaryKey("PK_sec_secret_references", x => new { x.installation_id, x.tenant_id, x.reference_id });
                    table.UniqueConstraint("AK_sec_secret_references_installation_id_tenant_id_profile_id_~", x => new { x.installation_id, x.tenant_id, x.profile_id, x.reference_id });
                    table.CheckConstraint("ck_sec_secret_references_installation_id", "installation_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_secret_references_locator_id", "locator_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_secret_references_profile_id", "profile_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_secret_references_reference_id", "reference_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_secret_references_tenant_id", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_sec_secret_references_sec_connection_profiles_installation_~",
                        columns: x => new { x.installation_id, x.tenant_id, x.profile_id },
                        principalSchema: "app",
                        principalTable: "sec_connection_profiles",
                        principalColumns: new[] { "installation_id", "tenant_id", "profile_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sec_binding_revisions",
                schema: "app",
                columns: table => new
                {
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_sec_binding_revisions", x => new { x.installation_id, x.tenant_id, x.binding_id, x.revision });
                    table.UniqueConstraint("AK_sec_binding_revisions_installation_id_tenant_id_target_id_c~", x => new { x.installation_id, x.tenant_id, x.target_id, x.consumer_id, x.operation, x.binding_id, x.revision });
                    table.CheckConstraint("ck_sec_binding_revisions_binding_id", "binding_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_binding_revisions_installation_id", "installation_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_binding_revisions_manifest_id", "manifest_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_binding_revisions_profile_id", "profile_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_binding_revisions_reference_id", "reference_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_binding_revisions_target_id", "target_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_binding_revisions_tenant_id", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_binding_state", "revision > 0 AND state IN ('active','revoked') AND consumer_id = 'inventory.readonly.v1' AND operation = 'target.read' AND isfinite(not_before_utc) AND isfinite(expires_at_utc) AND not_before_utc < expires_at_utc");
                    table.ForeignKey(
                        name: "FK_sec_binding_revisions_sec_connection_profiles_installation_~",
                        columns: x => new { x.installation_id, x.tenant_id, x.profile_id, x.target_id, x.consumer_id, x.operation },
                        principalSchema: "app",
                        principalTable: "sec_connection_profiles",
                        principalColumns: new[] { "installation_id", "tenant_id", "profile_id", "target_id", "consumer_id", "operation" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sec_binding_revisions_sec_secret_references_installation_id~",
                        columns: x => new { x.installation_id, x.tenant_id, x.profile_id, x.reference_id },
                        principalSchema: "app",
                        principalTable: "sec_secret_references",
                        principalColumns: new[] { "installation_id", "tenant_id", "profile_id", "reference_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sec_binding_heads",
                schema: "app",
                columns: table => new
                {
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    operation = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sec_binding_heads", x => new { x.installation_id, x.tenant_id, x.target_id, x.consumer_id, x.operation });
                    table.CheckConstraint("ck_sec_binding_heads_binding_id", "binding_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_binding_heads_installation_id", "installation_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_binding_heads_target_id", "target_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_binding_heads_tenant_id", "tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_sec_head_revision", "revision > 0");
                    table.ForeignKey(
                        name: "FK_sec_binding_heads_sec_binding_revisions_installation_id_ten~",
                        columns: x => new { x.installation_id, x.tenant_id, x.target_id, x.consumer_id, x.operation, x.binding_id, x.revision },
                        principalSchema: "app",
                        principalTable: "sec_binding_revisions",
                        principalColumns: new[] { "installation_id", "tenant_id", "target_id", "consumer_id", "operation", "binding_id", "revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sec_binding_heads_installation_id_binding_id",
                schema: "app",
                table: "sec_binding_heads",
                columns: new[] { "installation_id", "binding_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sec_binding_heads_installation_id_tenant_id_target_id_consu~",
                schema: "app",
                table: "sec_binding_heads",
                columns: new[] { "installation_id", "tenant_id", "target_id", "consumer_id", "operation", "binding_id", "revision" });

            migrationBuilder.CreateIndex(
                name: "IX_sec_binding_revisions_installation_id_manifest_id",
                schema: "app",
                table: "sec_binding_revisions",
                columns: new[] { "installation_id", "manifest_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sec_binding_revisions_installation_id_tenant_id_profile_id_~",
                schema: "app",
                table: "sec_binding_revisions",
                columns: new[] { "installation_id", "tenant_id", "profile_id", "reference_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sec_binding_revisions_installation_id_tenant_id_profile_id~1",
                schema: "app",
                table: "sec_binding_revisions",
                columns: new[] { "installation_id", "tenant_id", "profile_id", "target_id", "consumer_id", "operation" });

            migrationBuilder.CreateIndex(
                name: "IX_sec_connection_profiles_installation_id_profile_id",
                schema: "app",
                table: "sec_connection_profiles",
                columns: new[] { "installation_id", "profile_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sec_secret_references_installation_id_reference_id",
                schema: "app",
                table: "sec_secret_references",
                columns: new[] { "installation_id", "reference_id" },
                unique: true);
            migrationBuilder.Sql("""
                DO $sec$
                BEGIN
                  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'kordra_app') THEN
                    REVOKE INSERT, UPDATE, DELETE, TRUNCATE ON app.sec_connection_profiles, app.sec_secret_references, app.sec_binding_revisions, app.sec_binding_heads FROM kordra_app;
                    GRANT SELECT ON app.sec_connection_profiles, app.sec_secret_references, app.sec_binding_revisions, app.sec_binding_heads TO kordra_app;
                    REVOKE ALL ON app.sec_command_receipts, app.sec_ef_migrations_history FROM kordra_app;
                  END IF;
                END $sec$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new InvalidOperationException("SEC authority history cannot be dropped by a down migration.");
    }
}
