using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kordra.Modules.IdentityAccess.Infrastructure.Migrations;

[DbContext(typeof(KordraIdentityDbContext))]
[Migration("20261021130000_RestrictIdentityMigrationHistory")]
public sealed class RestrictIdentityMigrationHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            "REVOKE ALL PRIVILEGES ON TABLE app.iam_ef_migrations_history FROM PUBLIC, kordra_app;");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("IAM migration history must remain inaccessible to the runtime role.");
}
