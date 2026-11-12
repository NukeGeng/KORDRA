using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

public sealed class WorkflowPolicyMigrationTests
{
    [Fact]
    public void ExportPendingRegressionPolicyMigrationScriptsWithoutOpeningDatabase()
    {
        const string locator = "Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC";
        using var identity = new Kordra.Modules.IdentityAccess.Infrastructure.KordraIdentityDbContext(
            new DbContextOptionsBuilder<Kordra.Modules.IdentityAccess.Infrastructure.KordraIdentityDbContext>()
                .UseNpgsql(locator, o => o.MigrationsHistoryTable("iam_ef_migrations_history", "app")).Options);
        using var projects = new Kordra.Modules.ProjectEnvironment.Infrastructure.ProjectDbContext(
            new DbContextOptionsBuilder<Kordra.Modules.ProjectEnvironment.Infrastructure.ProjectDbContext>()
                .UseNpgsql(locator, o => o.MigrationsHistoryTable("prj_ef_migrations_history", "app")).Options);
        Export("iam.sql", identity, "20261022085218_AddResourcePolicies", "20261107115452_AddWorkflowPolicyActions");
        Export("prj.sql", projects, "20261023020110_StrengthenPolicyBindingHistoryScope", "20261107115454_AddWorkflowPolicyActions");

        static void Export(string name, DbContext context, string from, string to)
        {
            var sql = context.GetService<IMigrator>().GenerateScript(from, to);
            Assert.Contains(to, sql); Assert.Contains("ALTER TABLE app.", sql);
            Assert.DoesNotContain("CREATE TABLE app.", sql);
            Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UPDATE app.", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
            if (Environment.GetEnvironmentVariable("KORDRA_REGRESSION_POLICY_MIGRATION_EXPORT") is not { } directory) return;
            Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, name), sql);
        }
    }

    [Fact]
    public void PolicyUpgradeOnlyReplacesChecksAndRetainsLegacyAlternative()
    {
        Migration[] migrations =
        [
            new Kordra.Modules.IdentityAccess.Infrastructure.Migrations.AddWorkflowPolicyActions(),
            new Kordra.Modules.ProjectEnvironment.Infrastructure.Migrations.AddWorkflowPolicyActions()
        ];
        foreach (var migration in migrations)
        {
            Assert.All(migration.UpOperations, operation =>
                Assert.True(operation is AddCheckConstraintOperation or DropCheckConstraintOperation));
            foreach (var check in migration.UpOperations.OfType<AddCheckConstraintOperation>())
            {
                Assert.Contains("target.read", check.Sql);
                foreach (var action in new[] { "workflow.start", "workflow.read", "workflow.cancel", "workflow.reconcile" })
                    Assert.Contains(action, check.Sql);
                Assert.Contains("scope_kind = 'environment'", check.Sql);
            }
            Assert.All(migration.DownOperations.OfType<AddCheckConstraintOperation>(), check =>
            {
                Assert.Contains("target.read", check.Sql);
                Assert.DoesNotContain("workflow.", check.Sql);
            });
        }
    }
}
