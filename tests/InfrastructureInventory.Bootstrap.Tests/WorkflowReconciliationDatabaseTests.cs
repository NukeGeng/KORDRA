using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class WorkflowReconciliationDatabaseTests
{
    private static readonly Guid CaseId = Guid.Parse("0d14d775-3828-4fd0-9f0b-8046a0f3b8f2");
    private const string Schema = "t12_0d14d77538284fd09f0b8046a0f3b8f2";

    [Fact]
    public void ExportSqlWithoutDatabaseAccess()
    {
        using var connection = Connection("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC");
        var scripts = Scripts(connection, offlineMetadata: true);
        Assert.All(scripts.Values, sql => Assert.Contains(Schema, sql));
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        if (Environment.GetEnvironmentVariable("KORDRA_WFL_RECONCILE_EXPORT") is not { } directory) return;
        Directory.CreateDirectory(directory);
        foreach (var (name, sql) in scripts) File.WriteAllText(Path.Combine(directory, name), sql);
    }

    [WorkflowReconciliationDatabaseFact]
    public async Task DecisionAuditFloorCommitTogetherAndLateDenialsRollBack()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = budget.Token;
        var locator = Environment.GetEnvironmentVariable("KORDRA_WFL_RECONCILE_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Approved fixture locator required.");
        await using var setup = Connection(locator);
        var scripts = Scripts(setup);
        using var approved = JsonDocument.Parse(File.ReadAllBytes(Environment.GetEnvironmentVariable("KORDRA_WFL_RECONCILE_MANIFEST")!));
        Assert.Equal("approved_for_execution", approved.RootElement.GetProperty("status").GetString());
        Assert.Equal(CaseId, approved.RootElement.GetProperty("caseId").GetGuid());
        Assert.Equal(Schema, approved.RootElement.GetProperty("schema").GetString());
        foreach (var (name, sql) in scripts)
            Assert.Equal(approved.RootElement.GetProperty("sha256").GetProperty(name).GetString(),
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql))));
        await setup.OpenAsync(ct);
        Assert.Equal(0, await NamespaceCount(setup, ct));
        var created = false;
        try
        {
            await using (var ddl = await setup.BeginTransactionAsync(ct))
            {
                foreach (var name in new[] { "create.sql", "wfl.sql", "aud.sql" }) await Execute(setup, scripts[name], ct, ddl);
                await ddl.CommitAsync(ct); created = true;
            }
            await using var custody = await IsolatedWorkflowFixture.Custody(locator,
                CaseId, WorkflowFixtureCustodyLease.LockKey(CaseId), ct);
            Assert.NotNull(custody);
            var now = DateTimeOffset.UtcNow;
            var spec = new WorkflowSpecification(CaseId, CaseId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64),
                WorkflowEnvironment.Test, now, now.AddHours(1), now.AddMinutes(3));
            var command = new WorkflowReconciliationCommand(CaseId, CaseId, spec.InstanceId, Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(), 1, WorkflowReconciliationKind.ConfirmKnownRollback,
                new string('b', 64), spec.SpecificationHash);
            await using var writer = Connection(locator); await writer.OpenAsync(ct);
            await using (var context = IsolatedWorkflowFixture.CreateReconciliationFixture(writer, CaseId))
            await using (var root = await writer.BeginTransactionAsync(ct))
            {
                await context.Database.UseTransactionAsync(root, ct);
                await new WorkflowStore(context).StageCreationAsync(spec, now, ct);
                await root.CommitAsync(ct);
            }
            await AssertState(1, 0);
            var authorityChecks = 0;
            var lateRevocation = await Run(_ => Task.FromResult(true), _ => Task.FromResult(++authorityChecks == 1));
            Assert.Equal(WorkflowCommitDisposition.NoCommitAttempt, lateRevocation.Disposition);
            Assert.Null(lateRevocation.Receipt);
            await AssertState(1, 0);
            var evidenceChecks = 0;
            var changedEvidence = await Run(_ => Task.FromResult(++evidenceChecks == 1), _ => Task.FromResult(true));
            Assert.Equal(WorkflowCommitDisposition.NoCommitAttempt, changedEvidence.Disposition);
            await AssertState(1, 0);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Run(_ => Task.FromResult(false), _ => Task.FromResult(true)));
            await AssertState(1, 0);
            var closedDuringFinalCheck = await Run(_ => Task.FromResult(true), _ => Task.FromResult(true), true);
            Assert.Equal(WorkflowCommitDisposition.NoCommitAttempt, closedDuringFinalCheck.Disposition);
            await AssertState(1, 0);
            var committed = await Run(_ => Task.FromResult(true), _ => Task.FromResult(true));
            Assert.Equal(WorkflowCommitDisposition.Committed, committed.Disposition);
            Assert.NotNull(committed.Receipt);
            Assert.False(committed.Receipt.Duplicate);
            await AssertState(2, 1);
            var retry = await Run(_ => throw new InvalidOperationException("Retry must use the original receipt."), _ => Task.FromResult(true));
            Assert.Equal(WorkflowCommitDisposition.NoCommitAttempt, retry.Disposition);
            Assert.NotNull(retry.Receipt); Assert.True(retry.Receipt.Duplicate);
            await AssertState(2, 1);

            async Task<WorkflowReconciliationTransactionResult> Run(Func<CancellationToken, Task<bool>> evidence,
                Func<CancellationToken, Task<bool>> current, bool closeAtFinalAuthority = false)
            {
                var gate = new FairAsyncReaderWriterAdmissionGate();
                var checks = 0;
                async Task<bool> Current(CancellationToken token)
                {
                    var allowed = await current(token);
                    if (closeAtFinalAuthority && ++checks == 2) gate.Close();
                    return allowed;
                }
                await using var held = await gate.AcquireReadAsync(ct);
                await using var root = await writer.BeginTransactionAsync(ct);
                var mutation = new AuditIntent(1, Guid.NewGuid(), Guid.NewGuid(), AuditIntentStage.Mutation,
                    command.ActorId, CaseId, spec.InstanceId, "workflow.reconcile", "environment", spec.EnvironmentId,
                    1, 1, "workflow-reconcile-" + command.CommandHash, now.AddSeconds(1), command.RequestId);
                try
                {
                    var result = await IsolatedWorkflowFixture.Reconcile(writer, root, CaseId, command,
                        spec.ProjectId, spec.EnvironmentId, mutation, gate, custody, evidence, Current, ct);
                    Assert.Equal(closeAtFinalAuthority, gate.IsClosed); return result;
                }
                catch { Assert.False(gate.IsClosed); throw; }
            }

            async Task AssertState(long version, long count)
            {
                await using var query = new NpgsqlCommand($"SELECT version, (SELECT count(*) FROM {Schema}.operator_decisions), " +
                    $"(SELECT count(*) FROM {Schema}.aud_intents), (SELECT count(*) FROM {Schema}.clock_floor) " +
                    $"FROM {Schema}.instances WHERE instance_id=@id", setup);
                query.Parameters.AddWithValue("id", spec.InstanceId);
                await using var reader = await query.ExecuteReaderAsync(ct);
                Assert.True(await reader.ReadAsync(ct));
                Assert.Equal(version, reader.GetInt64(0));
                Assert.Equal(count, reader.GetInt64(1)); Assert.Equal(count, reader.GetInt64(2)); Assert.Equal(count, reader.GetInt64(3));
            }
        }
        finally
        {
            if (created)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await Execute(setup, scripts["cleanup.sql"], cleanup.Token);
                Assert.Equal(0, await NamespaceCount(setup, cleanup.Token));
                await using var released = await IsolatedWorkflowFixture.Custody(locator,
                    CaseId, WorkflowFixtureCustodyLease.LockKey(CaseId), cleanup.Token);
                Assert.NotNull(released);
            }
        }
    }

    internal static Dictionary<string, string> Scripts(NpgsqlConnection connection, bool offlineMetadata = false)
    {
        using var workflow = offlineMetadata ? WorkflowDbContext.CreateReconciliationFixture(connection, CaseId)
            : IsolatedWorkflowFixture.CreateReconciliationFixture(connection, CaseId);
        using var audit = offlineMetadata ? AuditDbContextFactory.CreateIsolatedTestContext(connection, CaseId)
            : IsolatedWorkflowFixture.AuditContext(connection, CaseId);
        return new() { ["create.sql"] = $"CREATE SCHEMA {Schema};\n", ["wfl.sql"] = workflow.Database.GenerateCreateScript(),
            ["aud.sql"] = audit.Database.GenerateCreateScript(), ["cleanup.sql"] = $"DROP SCHEMA {Schema} CASCADE;\n" };
    }
    private static NpgsqlConnection Connection(string cs) => new(new NpgsqlConnectionStringBuilder(cs)
        { Pooling = false, Multiplexing = false }.ConnectionString);
    private static async Task<long> NamespaceCount(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_namespace WHERE nspname=@schema", connection);
        command.Parameters.AddWithValue("schema", Schema); return (long)(await command.ExecuteScalarAsync(ct))!;
    }
    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken ct, NpgsqlTransaction? root = null)
    {
        await using var command = new NpgsqlCommand(sql, connection, root) { CommandTimeout = 3 }; await command.ExecuteNonQueryAsync(ct);
    }
}

public sealed class WorkflowReconciliationDatabaseFactAttribute : FactAttribute
{
    public WorkflowReconciliationDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KORDRA_WFL_RECONCILE_MANIFEST")))
            Skip = "Requires exact approval for the paired reconciliation PostgreSQL fixture.";
    }
}
