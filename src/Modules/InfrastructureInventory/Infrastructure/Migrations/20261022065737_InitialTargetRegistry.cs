using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kordra.Modules.InfrastructureInventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialTargetRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "inv_commands",
                schema: "app",
                columns: table => new
                {
                    key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    result_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inv_commands", x => x.key_hash);
                });

            migrationBuilder.CreateTable(
                name: "inv_targets",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    association_revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inv_targets", x => x.id);
                    table.CheckConstraint("ck_inv_revisions", "revision > 0 AND association_revision > 0");
                    table.CheckConstraint("ck_inv_status", "status IN ('active','disabled','deleted')");
                });
            migrationBuilder.Sql("REVOKE ALL PRIVILEGES ON TABLE app.inv_ef_migrations_history FROM PUBLIC, kordra_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Foundation owner history is additive; use a reviewed forward correction.");
    }
}
