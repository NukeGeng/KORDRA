using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kordra.Modules.ProjectEnvironment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialProjectCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "prj_commands",
                schema: "app",
                columns: table => new
                {
                    key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    result_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prj_commands", x => x.key_hash);
                });

            migrationBuilder.CreateTable(
                name: "prj_projects",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prj_projects", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_prj_projects_revision", "revision > 0");
                    table.CheckConstraint("ck_prj_projects_status", "status IN ('active','suspended','deleted')");
                });

            migrationBuilder.CreateTable(
                name: "prj_applications",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prj_applications", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_prj_applications_revision", "revision > 0");
                    table.CheckConstraint("ck_prj_applications_status", "status IN ('active','suspended','deleted')");
                    table.ForeignKey(
                        name: "FK_prj_applications_prj_projects_tenant_id_project_id",
                        columns: x => new { x.tenant_id, x.project_id },
                        principalSchema: "app",
                        principalTable: "prj_projects",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "prj_environments",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    classification = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    policy_revision = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prj_environments", x => new { x.tenant_id, x.id });
                    table.UniqueConstraint("AK_prj_environments_tenant_id_project_id_id", x => new { x.tenant_id, x.project_id, x.id });
                    table.CheckConstraint("ck_prj_classification", "classification IN ('Development','Test','Staging','Production')");
                    table.CheckConstraint("ck_prj_environments_revision", "revision > 0");
                    table.CheckConstraint("ck_prj_environments_status", "status IN ('active','suspended','deleted')");
                    table.CheckConstraint("ck_prj_policy_revision", "policy_revision > 0");
                    table.ForeignKey(
                        name: "FK_prj_environments_prj_projects_tenant_id_project_id",
                        columns: x => new { x.tenant_id, x.project_id },
                        principalSchema: "app",
                        principalTable: "prj_projects",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "prj_target_associations",
                schema: "app",
                columns: table => new
                {
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prj_target_associations", x => x.target_id);
                    table.CheckConstraint("ck_prj_association_revision", "revision > 0");
                    table.ForeignKey(
                        name: "FK_prj_target_associations_prj_environments_tenant_id_project_~",
                        columns: x => new { x.tenant_id, x.project_id, x.environment_id },
                        principalSchema: "app",
                        principalTable: "prj_environments",
                        principalColumns: new[] { "tenant_id", "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_prj_target_associations_prj_projects_tenant_id_project_id",
                        columns: x => new { x.tenant_id, x.project_id },
                        principalSchema: "app",
                        principalTable: "prj_projects",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_prj_applications_tenant_id_project_id",
                schema: "app",
                table: "prj_applications",
                columns: new[] { "tenant_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_prj_target_associations_tenant_id_project_id_environment_id",
                schema: "app",
                table: "prj_target_associations",
                columns: new[] { "tenant_id", "project_id", "environment_id" });
            migrationBuilder.Sql("REVOKE ALL PRIVILEGES ON TABLE app.prj_ef_migrations_history FROM PUBLIC, kordra_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Foundation owner history is additive; use a reviewed forward correction.");
    }
}
