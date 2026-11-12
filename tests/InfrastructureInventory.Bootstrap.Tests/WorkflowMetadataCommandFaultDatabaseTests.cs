using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Messaging.Tests;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

[Collection(LocalHostEnvironmentCollection.Name)]
public sealed class WorkflowMetadataCommandFaultDatabaseTests
{
    [Fact]
    public void ExportBothMetadataCommandFaultSchemasWithoutDatabaseAccess()
    {
        Export<CancellationCommandFaultCase>("cancel"); Export<ReconciliationCommandFaultCase>("reconcile");
        static void Export<TCase>(string action) where TCase : IWorkflowOperatorAuthorityCase
        {
            var scripts = Scripts<TCase>(); Assert.Equal(7, scripts.Count);
            foreach (var sql in scripts.Values)
            { Assert.Contains("t12_" + TCase.CaseId.ToString("N"), sql); Assert.DoesNotContain("\"app\".", sql); Assert.DoesNotContain("CREATE ROLE", sql, StringComparison.OrdinalIgnoreCase); }
            if (Environment.GetEnvironmentVariable("KORDRA_WFL_COMMAND_FAULT_EXPORT") is not { } directory) return;
            directory = Path.Combine(directory, action); Directory.CreateDirectory(directory);
            foreach (var (name, sql) in scripts) File.WriteAllText(Path.Combine(directory, name), sql);
        }
    }

    [WorkflowMetadataCommandFaultFact]
    public async Task RealAuthorityMetadataCommandsRetainExactReceiptsAfterActualCommitReplyLoss()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(90)); var ct = budget.Token;
        var locator = Environment.GetEnvironmentVariable("KORDRA_WFL_COMMAND_FAULT_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Exact metadata command fault locator required.");
        var settings = new NpgsqlConnectionStringBuilder(locator);
        Assert.Equal("127.0.0.1", settings.Host); Assert.Equal(5432, settings.Port);
        Assert.Equal(IsolatedWorkflowFixture.ExpectedDatabase, settings.Database); Assert.Equal("kordra_test", settings.Username);
        Assert.False(settings.Pooling); Assert.False(settings.Multiplexing);
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Environment.GetEnvironmentVariable("KORDRA_WFL_COMMAND_FAULT_MANIFEST")!));
        Assert.Equal("approved_for_execution", manifest.RootElement.GetProperty("status").GetString());
        await Run<CancellationCommandFaultCase>("cancel"); await Run<ReconciliationCommandFaultCase>("reconcile");

        async Task Run<TCase>(string action) where TCase : IWorkflowOperatorAuthorityCase
        {
            var caseId = TCase.CaseId; var schema = "t12_" + caseId.ToString("N"); var scripts = Scripts<TCase>();
            var approved = manifest.RootElement.GetProperty("cases").GetProperty(action);
            Assert.Equal(caseId, approved.GetProperty("caseId").GetGuid()); Assert.Equal(schema, approved.GetProperty("schema").GetString());
            foreach (var (name, sql) in scripts) Assert.Equal(approved.GetProperty("sha256").GetProperty(name).GetString(), Hash(Encoding.UTF8.GetBytes(sql)));
            var key = action == "reconcile" ? WorkflowFixtureCustodyLease.LockKey(caseId) : (long?)null;
            if (key is { } expectedKey) Assert.Equal(expectedKey, approved.GetProperty("advisoryKey").GetInt64());
            await using var setup = IsolatedWorkflowFixture.Connection(locator, caseId); await setup.OpenAsync(ct);
            Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM pg_namespace WHERE nspname=@schema", ct, ("schema", schema)));
            var created = false; WorkflowFixtureCustodyLease? custody = null;
            try
            {
                await using (var ddl = await setup.BeginTransactionAsync(ct))
                {
                    foreach (var (name, sql) in scripts.Where(x => x.Key != "cleanup.sql")) await Execute(setup, sql, ct, ddl);
                    await ddl.CommitAsync(ct); created = true;
                }
                var now = DateTimeOffset.UtcNow;
                var spec = new WorkflowSpecification(caseId, caseId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                    Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64),
                    WorkflowEnvironment.Test, now, now.AddMinutes(5), now.AddSeconds(30));
                var gate = new FairAsyncReaderWriterAdmissionGate();
                var authority = await WorkflowOperatorAuthorityFixture<TCase>.SeedAsync(setup, locator, spec, gate, ct, "workflow." + action);
                await using (var db = IsolatedWorkflowFixture.CreateReconciliationFixture(setup, caseId))
                await using (var baseline = await setup.BeginTransactionAsync(ct))
                {
                    await db.Database.UseTransactionAsync(baseline, ct);
                    await new WorkflowStore(db).StageCreationAsync(spec, now, ct); await baseline.CommitAsync(ct);
                }
                if (key is { } lockKey)
                { custody = await IsolatedWorkflowFixture.Custody(locator, caseId, lockKey, ct); Assert.NotNull(custody); }
                await using var held = await gate.AcquireReadAsync(ct);
                var scope = new WorkflowReadScope(caseId, spec.ProjectId, spec.EnvironmentId, spec.InstanceId);
                var original = await authority.Commands.ReadUnderAdmissionAsync(authority.Session, scope, "workflow." + action, ct);
                Assert.NotNull(original);
                var requestId = Guid.NewGuid(); var actor = authority.Session.PrincipalId;
                var cancel = new WorkflowCancellationIntent(caseId, caseId, spec.InstanceId, requestId, actor, 1, spec.SpecificationHash);
                var reconcile = new WorkflowReconciliationCommand(caseId, caseId, spec.InstanceId, requestId,
                    actor, Guid.NewGuid(), 1, WorkflowReconciliationKind.ConfirmKnownRollback, new string('b', 64), spec.SpecificationHash);
                var commandHash = action == "cancel" ? cancel.CommandHash : reconcile.CommandHash;
                var mutation = new AuditIntent(1, Guid.NewGuid(), Guid.NewGuid(), AuditIntentStage.Mutation, actor,
                    caseId, spec.InstanceId, "workflow." + action, "environment", spec.EnvironmentId,
                    original.Policy.PolicyRevision, original.Authority.Grant.AssignmentRevision,
                    "workflow-" + action + "-" + commandHash, original.ObservedAtUtc, requestId);
                var checks = 0; CommitReplyFault? fault = null; string? xid = null; DateTime server = default; int pid;
                var table = action == "cancel" ? "cancellation_intents" : "operator_decisions";
                await AssertState(1, 0);
                await using (var writer = IsolatedWorkflowFixture.Connection(locator, caseId))
                {
                    await writer.OpenAsync(ct); pid = writer.ProcessID;
                    await using var root = await writer.BeginTransactionAsync(ct);
                    async Task<bool> Current(CancellationToken token)
                    {
                        var current = await authority.Commands.ReadUnderAdmissionAsync(authority.Session, scope, "workflow." + action, token);
                        if (!original.IsCurrentSuccessor(current)) return false;
                        if (++checks == 2)
                        {
                            await using (var query = new NpgsqlCommand("SELECT pg_current_xact_id()::text, pg_postmaster_start_time()", writer, root))
                            await using (var read = await query.ExecuteReaderAsync(token))
                            { Assert.True(await read.ReadAsync(token)); xid = read.GetString(0); server = read.GetDateTime(1); }
                            Assert.Equal("in progress", await Scalar(setup, "SELECT pg_xact_status(@xid::xid8)::text", token, ("xid", xid)));
                            await AssertState(1, 0); fault = CommitReplyFault.Install(writer);
                        }
                        return true;
                    }
                    if (action == "cancel")
                    {
                        var result = await IsolatedWorkflowFixture.Cancel(writer, root, caseId,
                            cancel, spec.ProjectId, spec.EnvironmentId, mutation, gate, Current, ct);
                        Assert.Equal(WorkflowCommitDisposition.Unknown, result.Disposition); Assert.Null(result.Receipt);
                    }
                    else
                    {
                        // Synthetic evidence only isolates the metadata commit path. It does not replace the separately qualified original-origin reader.
                        var result = await IsolatedWorkflowFixture.Reconcile(writer, root, caseId,
                            reconcile, spec.ProjectId, spec.EnvironmentId, mutation, gate, custody!, _ => Task.FromResult(true), Current, ct);
                        Assert.Equal(WorkflowCommitDisposition.Unknown, result.Disposition); Assert.Null(result.Receipt);
                    }
                    Assert.Equal(2, checks); Assert.NotNull(fault); Assert.True(fault.Dropped); Assert.True(gate.IsClosed);
                }
                Assert.NotNull(xid);
                using (var terminal = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    terminal.CancelAfter(TimeSpan.FromSeconds(3));
                    while (await Scalar(setup, "SELECT count(*) FROM pg_stat_activity WHERE pid=@pid", terminal.Token, ("pid", pid)) is 1L)
                        await Task.Delay(10, terminal.Token);
                }
                Assert.Equal(server, await Scalar(setup, "SELECT pg_postmaster_start_time()", ct));
                Assert.Equal("committed", await Scalar(setup, "SELECT pg_xact_status(@xid::xid8)::text", ct, ("xid", xid)));
                await AssertState(2, 1);
                Assert.Equal(1L, await Scalar(setup, $"SELECT count(*) FROM {schema}.{table} WHERE installation_id=@case AND tenant_id=@case AND instance_id=@instance AND request_id=@request AND actor_id=@actor AND command_hash=@hash AND specification_hash=@spec", ct,
                    ("case", caseId), ("instance", spec.InstanceId), ("request", requestId), ("actor", actor), ("hash", commandHash), ("spec", spec.SpecificationHash)));
                Assert.Equal(1L, await Scalar(setup, $"SELECT count(*) FROM {schema}.aud_intents WHERE intent_id=@intent AND canonical_intent_hash=@hash", ct,
                    ("intent", mutation.IntentId), ("hash", Convert.FromHexString(AuditIntentCanonicalizer.ComputeHash(mutation)))));
                Assert.True(gate.IsClosed);
                // Neither independent observation nor a maintainer assertion reopens the original gate or retries this mutation.

                async Task AssertState(long version, long effects)
                {
                    Assert.Equal(version, await Scalar(setup, $"SELECT version FROM {schema}.instances WHERE instance_id=@id", ct, ("id", spec.InstanceId)));
                    foreach (var effect in new[] { table, "aud_intents", "clock_floor" }) Assert.Equal(effects, await Scalar(setup, $"SELECT count(*) FROM {schema}.{effect}", ct));
                }
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                if (custody is not null) await custody.DisposeAsync();
                if (created) await Execute(setup, scripts["cleanup.sql"], cleanup.Token);
                Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM pg_namespace WHERE nspname=@schema", cleanup.Token, ("schema", schema)));
            }
        }
    }

    private static Dictionary<string, string> Scripts<TCase>() where TCase : IWorkflowOperatorAuthorityCase
    {
        var caseId = TCase.CaseId; var schema = "t12_" + caseId.ToString("N");
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC");
        using var iam = WorkflowOperatorAuthorityFixture<TCase>.Identity(connection);
        using var ten = WorkflowOperatorAuthorityFixture<TCase>.Tenants(connection);
        using var prj = WorkflowOperatorAuthorityFixture<TCase>.Projects(connection);
        using var wfl = WorkflowDbContext.CreateReconciliationFixture(connection, caseId);
        using var aud = AuditDbContextFactory.CreateIsolatedTestContext(connection, caseId);
        return new() { ["create.sql"] = "CREATE SCHEMA " + schema + ";\n", ["iam.sql"] = iam.Database.GenerateCreateScript(),
            ["ten.sql"] = ten.Database.GenerateCreateScript(), ["prj.sql"] = prj.Database.GenerateCreateScript(),
            ["wfl.sql"] = wfl.Database.GenerateCreateScript(), ["aud.sql"] = aud.Database.GenerateCreateScript(),
            ["cleanup.sql"] = "DROP SCHEMA " + schema + " CASCADE;\n" };
    }
    private static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
    private static async Task<object?> Scalar(NpgsqlConnection connection, string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    { await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 2 }; foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value); return await command.ExecuteScalarAsync(ct); }
    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken ct, NpgsqlTransaction? root = null)
    { await using var command = new NpgsqlCommand(sql, connection, root) { CommandTimeout = 3 }; await command.ExecuteNonQueryAsync(ct); }
}

public sealed class WorkflowMetadataCommandFaultFactAttribute : FactAttribute
{
    public WorkflowMetadataCommandFaultFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KORDRA_WFL_COMMAND_FAULT_MANIFEST")))
            Skip = "Requires exact approved two-schema metadata command fault operation manifest.";
    }
}
