using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kordra.Modules.ReleaseManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RequireReleaseRetireReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_rel_revision_state",
                schema: "app",
                table: "rel_revisions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rel_revision_state",
                schema: "app",
                table: "rel_revisions",
                sql: "(state = 'Draft' AND spec_bytes IS NULL AND spec_hash IS NULL AND frozen_at_utc IS NULL AND retired_at_utc IS NULL AND retired_reason_code IS NULL) OR (state = 'Frozen' AND spec_bytes IS NOT NULL AND spec_hash IS NOT NULL AND octet_length(spec_hash) = 32 AND octet_length(spec_bytes) BETWEEN 1 AND 65536 AND frozen_at_utc IS NOT NULL AND retired_at_utc IS NULL AND retired_reason_code IS NULL) OR (state = 'Retired' AND spec_bytes IS NOT NULL AND spec_hash IS NOT NULL AND octet_length(spec_hash) = 32 AND octet_length(spec_bytes) BETWEEN 1 AND 65536 AND frozen_at_utc IS NOT NULL AND retired_at_utc IS NOT NULL AND retired_reason_code IS NOT NULL AND retired_reason_code IN ('obsolete','operator_cancelled'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new InvalidOperationException("REL history constraints cannot be weakened by migration rollback.");
    }
}
