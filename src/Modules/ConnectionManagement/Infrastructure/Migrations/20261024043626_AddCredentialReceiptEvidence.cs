using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kordra.Modules.ConnectionManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCredentialReceiptEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "audit_occurred_at_utc",
                schema: "app",
                table: "sec_command_receipts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new InvalidOperationException("SEC receipt evidence cannot be dropped by a down migration.");
    }
}
