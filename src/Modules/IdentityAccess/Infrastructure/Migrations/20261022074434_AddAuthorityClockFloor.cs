using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kordra.Modules.IdentityAccess.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorityClockFloor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "iam_authority_clock_floors",
                schema: "app",
                columns: table => new
                {
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    admitted_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_iam_authority_clock_floors", x => x.installation_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Authority history and clock floors are additive; use a reviewed forward correction.");
    }
}
