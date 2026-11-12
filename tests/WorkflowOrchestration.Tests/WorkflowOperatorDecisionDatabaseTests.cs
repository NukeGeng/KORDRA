using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Kordra.WorkflowOrchestration.Tests;

public sealed class WorkflowOperatorDecisionDatabaseTests
{
    private static readonly Guid CaseId = Guid.Parse("e1b49694-67ab-4b2f-bdea-aef02f4e721e");
    private const string Schema = "t12_e1b4969467ab4b2fbdeaaef02f4e721e";

    [Fact]
    public void ExportReviewedSqlWithoutDatabaseAccess()
    {
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC");
        var scripts = Scripts(connection, offlineMetadata: true);
        Assert.All(scripts.Values, sql => Assert.Contains(Schema, sql));
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        if (Environment.GetEnvironmentVariable("KORDRA_WFL_DECISION_EXPORT") is not { } directory) return;
        Directory.CreateDirectory(directory);
        foreach (var (name, sql) in scripts) File.WriteAllText(Path.Combine(directory, name), sql);
    }

    [WorkflowOperatorDecisionDatabaseFact]
    public async Task ReceiptAndOwnerCasCommitRollbackConflictAndScope()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = budget.Token;
        var locator = Environment.GetEnvironmentVariable("KORDRA_WFL_DECISION_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Approved fixture locator required.");
        var cs = new NpgsqlConnectionStringBuilder(locator) { Pooling = false, Multiplexing = false }.ConnectionString;
        await using var setup = WorkflowModelFixture.Connection(cs, CaseId);
        var scripts = Scripts(setup);
        using var approval = JsonDocument.Parse(File.ReadAllBytes(Environment.GetEnvironmentVariable("KORDRA_WFL_DECISION_MANIFEST")!));
        var approved = approval.RootElement;
        Assert.Equal("approved_for_execution", approved.GetProperty("status").GetString());
        Assert.Equal(CaseId, approved.GetProperty("caseId").GetGuid());
        Assert.Equal(Schema, approved.GetProperty("schema").GetString());
        foreach (var (name, sql) in scripts)
            Assert.Equal(approved.GetProperty("sha256").GetProperty(name).GetString(),
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql))));
        await setup.OpenAsync(ct);
        Assert.Equal(0, await NamespaceCount(setup, ct));
        var created = false;
        try
        {
            await using (var ddl = await setup.BeginTransactionAsync(ct))
            {
                foreach (var name in new[] { "create.sql", "baseline.sql", "expand.sql" })
                    await Execute(setup, scripts[name], ct, ddl);
                await ddl.CommitAsync(ct);
                created = true;
            }
            var now = DateTimeOffset.UtcNow;
            var spec = new WorkflowSpecification(CaseId, CaseId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64),
                WorkflowEnvironment.Test, now, now.AddHours(1), now.AddMinutes(3));
            var actor = Guid.NewGuid();
            var request = Guid.NewGuid();
            var operation = Guid.NewGuid();
            WorkflowReconciliationCommand Make(Guid? tenant = null, Guid? who = null, Guid? key = null, long version = 1,
                string? evidence = null) => new(CaseId, tenant ?? CaseId, spec.InstanceId, key ?? request, who ?? actor,
                    operation, version, WorkflowReconciliationKind.ConfirmKnownRollback, evidence ?? new string('b', 64), spec.SpecificationHash);
            await using var writer = WorkflowModelFixture.Connection(cs, CaseId);
            await writer.OpenAsync(ct);
            await using (var context = WorkflowModelFixture.CreateReconciliationFixture(writer, CaseId))
            await using (var root = await writer.BeginTransactionAsync(ct))
            {
                await context.Database.UseTransactionAsync(root, ct);
                await new WorkflowStore(context).StageCreationAsync(spec, now, ct);
                await root.CommitAsync(ct);
            }
            await AssertState(1, 0);
            await Stage(Make(), true, false);
            await AssertState(1, 0);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Stage(Make(), false, true));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Stage(Make(tenant: Guid.NewGuid()), true, true));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Stage(Make(), true, true, Guid.NewGuid()));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Stage(Make(version: 2), true, true));
            await AssertState(1, 0);
            var result = await Stage(Make(), true, true);
            Assert.False(result.Duplicate);
            Assert.Equal(2, result.Version);
            await AssertState(2, 1);
            var duplicate = await Stage(Make(), false, true);
            Assert.True(duplicate.Duplicate);
            Assert.Equal(2, duplicate.Version);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Stage(Make(who: Guid.NewGuid()), true, true));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Stage(Make(evidence: new string('c', 64)), true, true));
            await Assert.ThrowsAsync<DbUpdateException>(() => Stage(Make(key: Guid.NewGuid(), version: 2), true, true));
            await AssertState(2, 1);

            async Task<WorkflowStoredResult> Stage(WorkflowReconciliationCommand command, bool valid, bool commit, Guid? project = null)
            {
                await using var context = WorkflowModelFixture.CreateReconciliationFixture(writer, CaseId);
                await using var root = await writer.BeginTransactionAsync(ct);
                await context.Database.UseTransactionAsync(root, ct);
                var staged = await new WorkflowOperatorDecisionStore(context).StageAsync(command, project ?? spec.ProjectId,
                    spec.EnvironmentId, _ => Task.FromResult(valid), now.AddSeconds(1), ct);
                if (commit) await root.CommitAsync(ct); else await root.RollbackAsync(ct);
                return staged;
            }

            async Task AssertState(long version, long receipts)
            {
                await using var command = new NpgsqlCommand($"SELECT version, (SELECT count(*) FROM {Schema}.operator_decisions) FROM {Schema}.instances WHERE instance_id=@id", setup);
                command.Parameters.AddWithValue("id", spec.InstanceId);
                await using var reader = await command.ExecuteReaderAsync(ct);
                Assert.True(await reader.ReadAsync(ct));
                Assert.Equal(version, reader.GetInt64(0));
                Assert.Equal(receipts, reader.GetInt64(1));
                Assert.False(await reader.ReadAsync(ct));
            }
        }
        finally
        {
            if (created)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await Execute(setup, scripts["cleanup.sql"], cleanup.Token);
                Assert.Equal(0, await NamespaceCount(setup, cleanup.Token));
            }
        }
    }

    internal static Dictionary<string, string> Scripts(NpgsqlConnection connection, bool offlineMetadata = false)
    {
        using var baseline = offlineMetadata ? WorkflowDbContext.CreateStartFixture(connection, CaseId)
            : WorkflowModelFixture.CreateStartFixture(connection, CaseId);
        return new()
        {
            ["create.sql"] = $"CREATE SCHEMA {Schema};\n",
            ["baseline.sql"] = baseline.Database.GenerateCreateScript(),
            ["expand.sql"] = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "wfl-v5-operator-decisions.sql"))
                .Replace("wfl.", Schema + ".", StringComparison.Ordinal),
            ["cleanup.sql"] = $"DROP SCHEMA {Schema} CASCADE;\n"
        };
    }

    private static async Task<long> NamespaceCount(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_namespace WHERE nspname=@schema", connection);
        command.Parameters.AddWithValue("schema", Schema);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken ct, NpgsqlTransaction? root = null)
    {
        await using var command = new NpgsqlCommand(sql, connection, root) { CommandTimeout = 3 };
        await command.ExecuteNonQueryAsync(ct);
    }
}

public sealed class WorkflowOperatorDecisionDatabaseFactAttribute : FactAttribute
{
    public WorkflowOperatorDecisionDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KORDRA_WFL_DECISION_MANIFEST")))
            Skip = "Requires exact approval for the operator decision PostgreSQL fixture.";
    }
}
