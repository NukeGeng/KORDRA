using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kordra.Modules.IdentityAccess.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "iam_role_definitions",
                schema: "app",
                columns: table => new
                {
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    manifest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    rules_json = table.Column<string>(type: "jsonb", nullable: false),
                    is_retired = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_iam_role_definitions", x => new { x.definition_id, x.revision });
                    table.CheckConstraint("ck_iam_role_definition_revision", "revision > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_iam_role_definitions_manifest_id",
                schema: "app",
                table: "iam_role_definitions",
                column: "manifest_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Foundation authority history is additive; use a reviewed forward correction.");
    }
}
