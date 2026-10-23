using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kordra.Modules.IdentityAccess.Infrastructure.Migrations;

[DbContext(typeof(KordraIdentityDbContext))]
[Migration("20261020120000_InitialIdentityPersistence")]
public sealed class InitialIdentityPersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "app");

        migrationBuilder.CreateTable(
            name: "iam_principals",
            schema: "app",
            columns: table => new
            {
                principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                normalized_user_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                password_hash = table.Column<string>(type: "text", nullable: false),
                security_stamp = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                concurrency_stamp = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                account_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                revision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                lockout_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                access_failed_count = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_iam_principals", principal => principal.principal_id);
                table.CheckConstraint("ck_iam_principals_account_status", "account_status IN ('active', 'disabled')");
                table.CheckConstraint("ck_iam_principals_revision", "revision > 0");
            });

        migrationBuilder.CreateTable(
            name: "iam_sessions",
            schema: "app",
            columns: table => new
            {
                session_id = table.Column<Guid>(type: "uuid", nullable: false),
                session_reference_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                principal_revision = table.Column<long>(type: "bigint", nullable: false),
                boot_generation = table.Column<Guid>(type: "uuid", nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_activity_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                absolute_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                is_revoked = table.Column<bool>(type: "boolean", nullable: false),
                revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                revocation_reason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_iam_sessions", session => session.session_id);
                table.CheckConstraint("ck_iam_sessions_hash_length", "octet_length(session_reference_hash) = 32");
                table.CheckConstraint("ck_iam_sessions_principal_revision", "principal_revision > 0");
                table.CheckConstraint("ck_iam_sessions_expiry_after_creation", "absolute_expires_at_utc > created_at_utc");
                table.CheckConstraint("ck_iam_sessions_activity_after_creation", "last_activity_at_utc >= created_at_utc");
                table.CheckConstraint(
                    "ck_iam_sessions_revocation_state",
                    "(is_revoked AND revoked_at_utc IS NOT NULL) OR (NOT is_revoked AND revoked_at_utc IS NULL AND revocation_reason IS NULL)");
                table.ForeignKey(
                    name: "fk_iam_sessions_iam_principals_principal_id",
                    column: session => session.principal_id,
                    principalSchema: "app",
                    principalTable: "iam_principals",
                    principalColumn: "principal_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "uq_iam_principals_normalized_user_name",
            schema: "app",
            table: "iam_principals",
            column: "normalized_user_name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_iam_sessions_principal_active_expiry",
            schema: "app",
            table: "iam_sessions",
            columns: new[] { "principal_id", "is_revoked", "absolute_expires_at_utc" });

        migrationBuilder.CreateIndex(
            name: "uq_iam_sessions_session_reference_hash",
            schema: "app",
            table: "iam_sessions",
            column: "session_reference_hash",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException(
            "IAM persistence migrations are additive. Disable the local pilot instead of dropping account/session data.");
}
