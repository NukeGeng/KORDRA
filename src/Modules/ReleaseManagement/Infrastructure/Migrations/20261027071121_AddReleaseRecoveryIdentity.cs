using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kordra.Modules.ReleaseManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReleaseRecoveryIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "boot_id",
                schema: "app",
                table: "rel_commands",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "invocation_id",
                schema: "app",
                table: "rel_commands",
                type: "uuid",
                nullable: true);

            // Existing uncommitted synthetic test receipts remain readable but cannot establish recovery.
            // Every receipt inserted after this migration must bind its exact boot and invocation.
            migrationBuilder.Sql("""
                CREATE FUNCTION app.rel_require_recovery_identity() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.boot_id IS NULL OR NEW.invocation_id IS NULL OR
                       NEW.boot_id = '00000000-0000-0000-0000-000000000000'::uuid OR
                       NEW.invocation_id = '00000000-0000-0000-0000-000000000000'::uuid THEN
                        RAISE EXCEPTION 'REL recovery identity is required';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER rel_command_recovery_identity BEFORE INSERT ON app.rel_commands
                    FOR EACH ROW EXECUTE FUNCTION app.rel_require_recovery_identity();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new InvalidOperationException("REL immutable recovery evidence cannot be removed by migration rollback.");
    }
}
