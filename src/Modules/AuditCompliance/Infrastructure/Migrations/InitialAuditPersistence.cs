using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kordra.Modules.AuditCompliance.Infrastructure.Migrations;

[DbContext(typeof(AuditDbContext))]
[Migration("20261021120000_InitialAuditPersistence")]
public sealed class InitialAuditPersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "app");

        migrationBuilder.CreateTable(
            name: "aud_intents",
            schema: "app",
            columns: table => new
            {
                record_id = table.Column<Guid>(type: "uuid", nullable: false),
                schema_version = table.Column<int>(type: "integer", nullable: false),
                intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                invocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                stage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                subject_ref = table.Column<Guid>(type: "uuid", nullable: true),
                tenant_ref = table.Column<Guid>(type: "uuid", nullable: true),
                resource_ref = table.Column<Guid>(type: "uuid", nullable: true),
                permission = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                scope_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                scope_id = table.Column<Guid>(type: "uuid", nullable: true),
                policy_revision = table.Column<long>(type: "bigint", nullable: true),
                authority_revision = table.Column<long>(type: "bigint", nullable: true),
                safe_reason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                result_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                result_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                canonical_intent = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                canonical_intent_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_aud_intents", entry => entry.record_id);
                table.CheckConstraint("ck_aud_intents_schema_version", "schema_version = 1");
                table.CheckConstraint("ck_aud_intents_stage", "stage IN ('U', 'D', 'mutation', 'outcome')");
                table.CheckConstraint("ck_aud_intents_hash_length", "octet_length(canonical_intent_hash) = 32");
                table.CheckConstraint("ck_aud_intents_canonical_length", "octet_length(canonical_intent) <= 8192");
                table.CheckConstraint("ck_aud_intents_revisions", "(policy_revision IS NULL OR policy_revision > 0) AND (authority_revision IS NULL OR authority_revision > 0)");
                table.CheckConstraint(
                    "ck_aud_intents_result_digest_size",
                    "(stage = 'D' AND result_digest IS NOT NULL AND length(result_digest) = 64 AND result_size_bytes IS NOT NULL AND result_size_bytes >= 0) OR (stage <> 'D' AND result_digest IS NULL AND result_size_bytes IS NULL)");
            });

        migrationBuilder.CreateIndex(
            name: "uq_aud_intents_intent_id",
            schema: "app",
            table: "aud_intents",
            column: "intent_id",
            unique: true);

        migrationBuilder.Sql(
            "REVOKE ALL PRIVILEGES ON TABLE app.aud_intents FROM kordra_app; " +
            "GRANT SELECT, INSERT ON TABLE app.aud_intents TO kordra_app; " +
            "REVOKE ALL PRIVILEGES ON TABLE app.aud_ef_migrations_history FROM kordra_app;");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException(
            "AUD persistence migrations are additive. Audit history is retained; disable the local pilot instead of dropping it.");
}
