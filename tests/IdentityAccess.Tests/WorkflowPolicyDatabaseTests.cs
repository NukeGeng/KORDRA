using System.Security.Cryptography;
using System.Text;
using Kordra.Modules.IdentityAccess.Authorization;
using Kordra.Modules.IdentityAccess.Contracts;
using Kordra.Modules.IdentityAccess.Infrastructure;
using Kordra.Modules.ProjectEnvironment;
using Kordra.Modules.ProjectEnvironment.Contracts;
using Kordra.Modules.ProjectEnvironment.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Xunit;

namespace Kordra.Modules.IdentityAccess.Tests;

public sealed class WorkflowPolicyDatabaseTests
{
    internal const string Schema = "t12_188a517c83bd47ae8d93264c59f738d0";
    private const string Synthetic = "Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC";

    [Fact]
    public void ExportUpgradeSqlWithoutConnection()
    {
        using var connection = new NpgsqlConnection(Synthetic);
        var scripts = Scripts(connection);
        Assert.All(scripts.Values, sql => Assert.Contains(Schema, sql));
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        if (Environment.GetEnvironmentVariable("KORDRA_POLICY_V2_EXPORT") is not { } directory) return;
        Directory.CreateDirectory(directory);
        foreach (var (name, sql) in scripts) File.WriteAllText(Path.Combine(directory, name), sql);
    }

    [WorkflowPolicyDatabaseFact]
    public async Task UpgradeRetainsV1AndScopesV2BindingsOnCallerRoot()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = budget.Token;
        var locator = Environment.GetEnvironmentVariable("KORDRA_POLICY_V2_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Private approved policy fixture locator required.");
        TestDatabaseConfiguration.ValidateFixtureLocator(locator);
        var target = new NpgsqlConnectionStringBuilder(locator);
        Assert.Equal("127.0.0.1", target.Host);
        Assert.Equal(5432, target.Port);
        Assert.Equal(TestDatabaseConfiguration.ExpectedFixtureDatabase, target.Database);
        Assert.Equal("kordra_test", target.Username);
        target.Pooling = false;
        await using var connection = new NpgsqlConnection(target.ConnectionString);
        var scripts = Scripts(connection);
        using var approval = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(
            Environment.GetEnvironmentVariable("KORDRA_POLICY_V2_MANIFEST")!));
        Assert.Equal("approved_for_execution", approval.RootElement.GetProperty("status").GetString());
        Assert.Equal(Schema, approval.RootElement.GetProperty("schema").GetString());
        foreach (var (name, sql) in scripts)
            Assert.Equal(approval.RootElement.GetProperty("sha256").GetProperty(name).GetString(),
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql))));
        await connection.OpenAsync(ct);
        Assert.Equal(0L, await CountNamespace());
        var created = false;
        try
        {
            await using (var setup = await connection.BeginTransactionAsync(ct))
            {
                await Sql("CREATE SCHEMA " + Schema, setup);
                await Sql(scripts["iam-baseline.sql"], setup);
                await Sql(scripts["prj-baseline.sql"], setup);
                await setup.CommitAsync(ct);
                created = true;
            }
            var tenant = Guid.NewGuid();
            var project = Guid.NewGuid();
            var environment = Guid.NewGuid();
            var now = new DateTimeOffset(2026, 11, 7, 0, 0, 0, TimeSpan.Zero);
            var lease = new Lease();
            Guid legacyId;
            await using (var root = await connection.BeginTransactionAsync(ct))
            {
                await using var iam = Identity(connection);
                await iam.Database.UseTransactionAsync(root, ct);
                var legacy = await new ResourcePolicyStore(iam).StagePublicationAsync(lease, tenant, project, null, 0,
                    new(1, "target.read", "environment", "active", now, null, [new("Test", "allow")]), now, ct);
                Assert.NotNull(legacy);
                legacyId = legacy.PolicyId;
                await iam.SaveChangesAsync(ct);
                await root.CommitAsync(ct);
            }
            await using (var upgrade = await connection.BeginTransactionAsync(ct))
            {
                await Sql(scripts["iam-upgrade.sql"], upgrade);
                await Sql(scripts["prj-upgrade.sql"], upgrade);
                await upgrade.CommitAsync(ct);
            }
            await using (var iam = Identity(connection))
                Assert.NotNull(await new ResourcePolicyStore(iam).ReadCurrentUnderAdmissionAsync(tenant, project, legacyId, 1, ct));

            foreach (var commit in new[] { false, true })
            {
                await using var root = await connection.BeginTransactionAsync(ct);
                await using var iam = Identity(connection);
                await using var prj = Projects(connection);
                await iam.Database.UseTransactionAsync(root, ct);
                await prj.Database.UseTransactionAsync(root, ct);
                prj.Projects.Add(new() { Id = project, TenantId = tenant, Name = "Synthetic" });
                prj.Environments.Add(new() { Id = environment, TenantId = tenant, ProjectId = project, Name = "Synthetic", Classification = "Test" });
                await prj.SaveChangesAsync(ct);
                var store = new ResourcePolicyStore(iam);
                foreach (var action in new[] { "workflow.start", "workflow.read", "workflow.cancel", "workflow.reconcile" })
                {
                    var payload = new ResourcePolicyPayload(2, action, "environment", "active", now, null, [new("Test", "allow")]);
                    var policy = await store.StagePublicationAsync(lease, tenant, project, null, 0, payload, now, ct);
                    Assert.NotNull(policy);
                    await iam.SaveChangesAsync(ct);
                    var bindings = new PolicyBindingStore(prj);
                    Assert.NotNull(await bindings.StageAsync(tenant, project, new(null, "environment", environment, 0, action),
                        new(policy.PolicyId, 1), Guid.NewGuid(), true, ct));
                    await prj.SaveChangesAsync(ct);
                    Assert.Equal(action, (await bindings.ReadCurrentForActionUnderAdmissionAsync(tenant, project, environment, action, ct))!.Action);
                    Assert.Null(await bindings.ReadCurrentUnderAdmissionAsync(tenant, project, environment, ct));
                    Assert.Null(await store.StagePublicationAsync(lease, tenant, project, policy.PolicyId, 1,
                        payload with { Action = "workflow.unknown" }, now, ct));
                    var alternate = action == "workflow.start" ? "workflow.read" : "workflow.start";
                    Assert.Null(await store.StagePublicationAsync(lease, tenant, project, policy.PolicyId, 1,
                        payload with { Action = alternate }, now, ct));
                }
                if (commit) await root.CommitAsync(ct); else await root.RollbackAsync(ct);
            }
            await using var count = new NpgsqlCommand($"SELECT count(*) FROM {Schema}.prj_policy_bindings", connection);
            Assert.Equal(4L, await count.ExecuteScalarAsync(ct));
        }
        finally
        {
            if (created)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await using var drop = new NpgsqlCommand(scripts["cleanup.sql"], connection);
                await drop.ExecuteNonQueryAsync(cleanup.Token);
                Assert.Equal(0L, await CountNamespace(cleanup.Token));
            }
        }

        async Task Sql(string sql, NpgsqlTransaction root)
        {
            await using var command = new NpgsqlCommand(sql, connection, root) { CommandTimeout = 3 };
            await command.ExecuteNonQueryAsync(ct);
        }
        async Task<long> CountNamespace(CancellationToken token = default)
        {
            await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_namespace WHERE nspname=@schema", connection);
            command.Parameters.AddWithValue("schema", Schema);
            return (long)(await command.ExecuteScalarAsync(token))!;
        }
    }

    private static KordraIdentityDbContext Identity(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<KordraIdentityDbContext>().UseNpgsql(connection)
            .ReplaceService<IModelCustomizer, FixtureCustomizer>().Options);
    private static ProjectDbContext Projects(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<ProjectDbContext>().UseNpgsql(connection)
            .ReplaceService<IModelCustomizer, FixtureCustomizer>().Options);

    private static Dictionary<string, string> Scripts(NpgsqlConnection connection)
    {
        using var iam = Identity(connection);
        using var prj = Projects(connection);
        var result = new Dictionary<string, string>();
        Add(iam, new Kordra.Modules.IdentityAccess.Infrastructure.Migrations.AddWorkflowPolicyActions(), "iam");
        Add(prj, new Kordra.Modules.ProjectEnvironment.Infrastructure.Migrations.AddWorkflowPolicyActions(), "prj");
        result["cleanup.sql"] = $"DROP SCHEMA {Schema} CASCADE;\n";
        return result;

        void Add(DbContext context, Migration migration, string prefix)
        {
            var baseline = context.Database.GenerateCreateScript();
            foreach (var old in migration.DownOperations.OfType<AddCheckConstraintOperation>())
            {
                var current = migration.UpOperations.OfType<AddCheckConstraintOperation>().Single(x => x.Name == old.Name);
                Assert.Contains(current.Sql, baseline);
                baseline = baseline.Replace(current.Sql, old.Sql, StringComparison.Ordinal);
            }
            result[prefix + "-baseline.sql"] = baseline;
            var commands = context.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, context.Model);
            result[prefix + "-upgrade.sql"] = string.Join("\n", commands.Select(x => x.CommandText))
                .Replace("app.", Schema + ".", StringComparison.Ordinal);
        }
    }

    public sealed class FixtureCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.HasDefaultSchema(Schema);
            foreach (var entity in modelBuilder.Model.GetEntityTypes()) entity.SetSchema(Schema);
        }
    }

    private sealed class Lease : IOfflineProvisioningLease { public void RequireHeld() { } }
}

public sealed class WorkflowPolicyDatabaseFactAttribute : FactAttribute
{
    public WorkflowPolicyDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KORDRA_POLICY_V2_MANIFEST")))
            Skip = "Requires exact approval for isolated workflow policy upgrade fixture.";
    }
}
