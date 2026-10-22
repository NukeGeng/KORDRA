using Kordra.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kordra.Modules.IdentityAccess.Infrastructure.Migrations;

[DbContext(typeof(KordraIdentityDbContext))]
[Migration("20261021143000_AddAuthenticationCommands")]
public sealed class AddAuthenticationCommands : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "iam_authentication_commands",
            schema: "app",
            columns: table => new
            {
                command_id = table.Column<Guid>(type: "uuid", nullable: false),
                idempotency_key_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                command_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_iam_authentication_commands", command => command.command_id);
                table.CheckConstraint(
                    "ck_iam_authentication_commands_hash_length",
                    "octet_length(idempotency_key_hash) = 32");
                table.CheckConstraint(
                    "ck_iam_authentication_commands_kind",
                    "command_kind IN ('logout-all', 'password-change')");
                table.ForeignKey(
                    name: "fk_iam_authentication_commands_iam_principals_principal_id",
                    column: command => command.principal_id,
                    principalSchema: "app",
                    principalTable: "iam_principals",
                    principalColumn: "principal_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "uq_iam_authentication_commands_idempotency_key_hash",
            schema: "app",
            table: "iam_authentication_commands",
            column: "idempotency_key_hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_iam_authentication_commands_principal_id",
            schema: "app",
            table: "iam_authentication_commands",
            column: "principal_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException(
            "IAM authentication command receipts are additive. Do not drop local account history during rollback.");
}
