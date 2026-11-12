using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Messaging.Tests;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Recovery;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Kordra.Modules.Messaging.Infrastructure.Transactions;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Weasel.Core.Migrations;
using Wolverine;
using Wolverine.Persistence.Durability;
using Wolverine.RDBMS;
using Wolverine.Runtime;
using Wolverine.Runtime.Serialization;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class WorkflowWakeFaultDatabaseTests
{
    public static readonly Guid CaseId = Guid.Parse("6e12cb57-58c0-438d-b48d-a42f25277e7c");
    private static string Schema => "t12_" + CaseId.ToString("N");
    private static string DirectoryPath => Path.Combine(Root(), ".amr-local", "t12-remaining-design", "t12-2d-wake-fault-schema-review");

    [Fact]
    public void ExportFaultSqlMetadataWithoutStartingHostsOrOpeningConnections()
    {
        const string locator = "Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC";
        using var connection = new NpgsqlConnection(locator);
        using var workflow = WorkflowDbContext.CreateWakeFixture(connection, CaseId);
        using var audit = AuditDbContextFactory.CreateIsolatedTestContext(connection, CaseId);
        using var host = WorkflowWakeFixtureProfile.Builder("Host=127.0.0.1;Port=1;Database=not_opened;Username=not_opened;Timeout=1",
            Schema, new(CaseId, ""), false).Build();
        WorkflowTransactionSchemaTests.ConfigureTransportMetadata(host);
        var native = Assert.IsAssignableFrom<IDatabase>(host.Services.GetRequiredService<IMessageStore>());
        var nativeSql = WorkflowTransactionSchemaTests.StableNativeScript(native, Schema);
        var scripts = new Dictionary<string, string> {
            ["wfl.sql"] = workflow.Database.GenerateCreateScript(), ["aud.sql"] = audit.Database.GenerateCreateScript(),
            ["msg-owned.sql"] = OwnedMessagingSchema.TablesSql.Replace("msg.", Schema + ".", StringComparison.Ordinal),
            ["msg-native.sql"] = nativeSql, ["cleanup.sql"] = "DROP SCHEMA " + Schema + " CASCADE;\n"
        };
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        foreach (var sql in scripts.Values) { Assert.Contains(Schema, sql); Assert.DoesNotContain("wolverine_queues", sql); }
        if (Environment.GetEnvironmentVariable("KORDRA_WFL_WAKE_FAULT_ARTIFACT_DIRECTORY") is not { } directory) return;
        Assert.Equal(DirectoryPath, Path.GetFullPath(directory)); Directory.CreateDirectory(directory);
        foreach (var (name, sql) in scripts)
        {
            var file = Path.Combine(directory, name);
            if (File.Exists(file)) Assert.Equal(sql, File.ReadAllText(file)); else File.WriteAllText(file, sql);
        }
        var manifest = JsonSerializer.Serialize(new { status = "proposal_not_operation_authorization", caseId = CaseId, schema = Schema,
            sha256 = scripts.ToDictionary(x => x.Key, x => Hash(Encoding.UTF8.GetBytes(x.Value))) }, new JsonSerializerOptions { WriteIndented = true });
        var path = Path.Combine(directory, "manifest.json");
        if (File.Exists(path)) Assert.Equal(manifest, File.ReadAllText(path)); else File.WriteAllText(path, manifest);
    }

    [WorkflowWakeFaultDatabaseFact]
    public async Task ExactRootProbeSeparatesRollbackBudgetExpiryAndLostCommitReply()
    {
        var scripts = ReadApprovedScripts();
        var locator = Environment.GetEnvironmentVariable("KORDRA_WFL_WAKE_FAULT_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Approved private fault fixture locator required.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90)); var ct = timeout.Token;
        var state = new WorkflowWakeFixtureState(CaseId, locator);
        await using var setup = state.NewOwnedConnection(); await setup.OpenAsync(ct);
        Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM pg_namespace WHERE nspname=@schema", ct, ("schema", Schema)));
        using var host = WorkflowWakeFixtureProfile.Builder(locator, Schema, state, false).Build();
        var created = false; var started = false;
        try
        {
            await using (var ddl = await setup.BeginTransactionAsync(ct))
            {
                await Execute(setup, "CREATE SCHEMA " + Schema, ct, ddl);
                foreach (var name in new[] { "wfl.sql", "aud.sql", "msg-owned.sql", "msg-native.sql" }) await Execute(setup, scripts[name], ct, ddl);
                await ddl.CommitAsync(ct); created = true;
            }
            var (spec, timer) = await WorkflowWakeDatabaseTests.SeedWaitingTimer(setup, ct, CaseId);
            var occurred = DateTimeOffset.UtcNow;
            var wake = new WorkflowWakeOperation(spec, timer.StepId, timer.TimerId, 1, 1, 1,
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, 3, occurred, occurred.AddSeconds(120));
            var registry = WorkflowWakePublication.CreateRegistry(new WorkflowWakeFixtureHandler.RefuseRequest());
            var request = WorkflowWakePublication.Prepare(spec, wake, registry, occurred);
            started = true; await host.StartAsync(ct);
            var runtime = host.Services.GetRequiredService<IWolverineRuntime>();
            var gate = new FairAsyncReaderWriterAdmissionGate();
            await using var recovery = IsolatedWorkflowFixture.Recovery(setup, CaseId);
            WorkflowTransactionOrigin rolledBack;
            var audit = Audit(spec, DateTimeOffset.UtcNow);
            await using (var connection = state.NewOwnedConnection())
            {
                await connection.OpenAsync(ct); await using var transaction = await connection.BeginTransactionAsync(ct);
                await using var root = await IsolatedWorkflowFixture.CreateIsolatedWakeTestAsync(connection, transaction, CaseId, new MessageContext(runtime), gate, ct);
                var admittedUtc = DateTimeOffset.UtcNow;
                await root.StageInitialTimerDispatchAsync(spec, wake, registry, 4, 1, Guid.NewGuid(), Guid.NewGuid(), admittedUtc, ct);
                await root.StageAuditAsync(audit, ct); await root.StageClockFloorAsync(admittedUtc, ct);
                rolledBack = await root.CaptureTimerCommitOriginAsync(ct);
                Assert.Same(rolledBack, await root.CaptureTimerCommitOriginAsync(ct));
                var pending = await IsolatedWorkflowFixture.Probe(rolledBack, setup, ct);
                Assert.Equal(WorkflowTransactionProbeStatus.Unresolved, pending.Status); Assert.True(pending.OriginalBackendPresent);
                Assert.Equal(WorkflowTransactionProbeStatus.Unavailable, (await IsolatedWorkflowFixture.Probe(rolledBack, connection, ct)).Status);
                Assert.Equal(WorkflowReceiptInspectionStatus.StillUnknown, await recovery.InspectSchedulerAsync(spec, wake, request, audit, ct));
                await root.RollbackAsync();
                Assert.Equal(WorkflowTransactionProbeStatus.Aborted, (await IsolatedWorkflowFixture.Probe(rolledBack, setup, ct)).Status);
                await Assert.ThrowsAsync<InvalidOperationException>(() => root.FlushConfirmedAsync());
            }
            Assert.False((await ObserveAfterClose(rolledBack)).OriginalBackendPresent);
            await AssertUnchanged();
            // A synthetic TimeProvider advances only this fixture clock; no system or database clock changes.
            var time = new ManualClock(DateTimeOffset.UtcNow); var clock = new WorkflowTrustedClock(time);
            clock.AdoptPersistedFloor(occurred.AddHours(-3));
            var budget = clock.BeginOperation(wake, TimeSpan.FromMilliseconds(200));
            await using (var connection = state.NewOwnedConnection())
            {
                await connection.OpenAsync(ct); await using var transaction = await connection.BeginTransactionAsync(ct);
                await using var root = await IsolatedWorkflowFixture.CreateIsolatedWakeTestAsync(connection, transaction, CaseId, new MessageContext(runtime), gate, ct);
                await root.StageInitialTimerDispatchAsync(spec, wake, registry, 4, 1, Guid.NewGuid(), Guid.NewGuid(), budget.AdmittedUtc, ct);
                await root.StageAuditAsync(audit, ct); await root.StageCheckedTimerClockFloorAsync(budget, ct);
                var origin = await root.CaptureTimerCommitOriginAsync(ct);
                time.Advance(TimeSpan.FromMilliseconds(300));
                Assert.Equal(WorkflowCommitDisposition.NoCommitAttempt, await root.CommitAsync(ct));
                Assert.Equal(WorkflowTransactionProbeStatus.Aborted, (await IsolatedWorkflowFixture.Probe(origin, setup, ct)).Status);
                await Assert.ThrowsAsync<InvalidOperationException>(() => root.FlushConfirmedAsync());
                Assert.Equal(time.GetUtcNow(), clock.GetUtcNow());
            }
            await AssertUnchanged();
            await using (var stillOpen = await gate.AcquireReadAsync(ct)) { }
            WorkflowTransactionOrigin committed;
            // Lose the actual backend COMMIT response, never fabricate an outcome or rerun the mutation.
            await using (var connection = state.NewOwnedConnection())
            {
                await connection.OpenAsync(ct); await using var transaction = await connection.BeginTransactionAsync(ct);
                await using var root = await IsolatedWorkflowFixture.CreateIsolatedWakeTestAsync(connection, transaction, CaseId, new MessageContext(runtime), gate, ct);
                var actualClock = new WorkflowTrustedClock(TimeProvider.System);
                var actualBudget = actualClock.BeginOperation(wake, TimeSpan.FromSeconds(10));
                await root.StageInitialTimerDispatchAsync(spec, wake, registry, 4, 1, Guid.NewGuid(), Guid.NewGuid(), actualBudget.AdmittedUtc, ct);
                await root.StageAuditAsync(audit, ct); await root.StageCheckedTimerClockFloorAsync(actualBudget, ct);
                committed = await root.CaptureTimerCommitOriginAsync(ct);
                Assert.Equal(WorkflowTransactionProbeStatus.Unresolved, (await IsolatedWorkflowFixture.Probe(committed, setup, ct)).Status);
                var fault = CommitReplyFault.Install(connection);
                Assert.Equal(WorkflowCommitDisposition.Unknown, await root.CommitAsync(ct)); Assert.True(fault.Dropped);
                await Assert.ThrowsAsync<InvalidOperationException>(() => root.FlushConfirmedAsync());
            }
            var observed = await ObserveAfterClose(committed);
            Assert.Equal(WorkflowTransactionProbeStatus.Committed, observed.Status); Assert.False(observed.OriginalBackendPresent);
            Assert.Equal(WorkflowReceiptInspectionStatus.MatchingObserved, await recovery.InspectSchedulerAsync(spec, wake, request, audit, ct));
            Assert.Equal(WorkflowReceiptInspectionStatus.MatchingObserved,
                await recovery.ReconcileSchedulerAsync(committed, spec, wake, request, audit, ct));
            Assert.Equal(WorkflowReceiptInspectionStatus.Conflict,
                await recovery.ReconcileSchedulerAsync(rolledBack, spec, wake, request, audit, ct));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await gate.AcquireReadAsync(ct));
            Assert.Equal(5L, await Scalar(setup, "SELECT version FROM " + Schema + ".instances WHERE instance_id=@id", ct, ("id", spec.InstanceId)));
            Assert.Equal(1L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".wake_operations", ct));
            Assert.Equal(1L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".publication_receipts WHERE operation_id=@id", ct, ("id", wake.WakeOperationId)));
            Assert.Equal(1L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".aud_intents WHERE intent_id=@id", ct, ("id", audit.IntentId)));
            Assert.Equal(WorkflowTransactionProbeStatus.Aborted, (await IsolatedWorkflowFixture.Probe(rolledBack, setup, ct)).Status);
            // Matching/terminal observations leave admission closed; they do not grant replay/takeover.
            async Task<WorkflowTransactionObservation> ObserveAfterClose(WorkflowTransactionOrigin origin)
            {
                using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct); bounded.CancelAfter(TimeSpan.FromSeconds(2));
                while (true)
                {
                    var observation = await IsolatedWorkflowFixture.Probe(origin, setup, bounded.Token);
                    if (observation.OriginalBackendPresent != true) return observation;
                    await Task.Delay(20, bounded.Token);
                }
            }
            async Task AssertUnchanged()
            {
                Assert.Equal(4L, await Scalar(setup, "SELECT version FROM " + Schema + ".instances WHERE instance_id=@id", ct, ("id", spec.InstanceId)));
                Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".wake_operations", ct));
                Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".publication_receipts", ct));
                Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".aud_intents", ct));
                Assert.Equal(WorkflowReceiptInspectionStatus.StillUnknown, await recovery.InspectSchedulerAsync(spec, wake, request, audit, ct));
            }
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            if (started) await host.StopAsync(cleanup.Token);
            if (created)
            {
                await Execute(setup, scripts["cleanup.sql"], cleanup.Token);
                Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM pg_namespace WHERE nspname=@schema", cleanup.Token, ("schema", Schema)));
            }
        }
    }

    private sealed class ManualClock(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _utc = initial;
        private long _stamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => _utc;
        public override long GetTimestamp() => _stamp;
        public void Advance(TimeSpan elapsed) { _utc += elapsed; _stamp += elapsed.Ticks; }
    }

    private static Dictionary<string, string> ReadApprovedScripts()
    {
        var file = Environment.GetEnvironmentVariable("KORDRA_WFL_WAKE_FAULT_FIXTURE_MANIFEST")
            ?? throw new InvalidOperationException("Exact fault fixture approval required.");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(file)); var root = manifest.RootElement;
        Assert.Equal("approved_for_execution", root.GetProperty("status").GetString()); Assert.Equal(CaseId, root.GetProperty("caseId").GetGuid());
        Assert.Equal(Schema, root.GetProperty("schema").GetString());
        var scripts = new Dictionary<string, string>();
        foreach (var name in new[] { "wfl.sql", "aud.sql", "msg-owned.sql", "msg-native.sql", "cleanup.sql" })
        {
            var bytes = File.ReadAllBytes(Path.Combine(DirectoryPath, name)); Assert.Equal(root.GetProperty("sha256").GetProperty(name).GetString(), Hash(bytes));
            scripts.Add(name, Encoding.UTF8.GetString(bytes));
        }
        Assert.Equal("DROP SCHEMA " + Schema + " CASCADE;\n", scripts["cleanup.sql"]); return scripts;
    }
    private static AuditIntent Audit(WorkflowSpecification spec, DateTimeOffset now) => new(1, Guid.NewGuid(), Guid.NewGuid(), AuditIntentStage.Mutation,
        Guid.NewGuid(), CaseId, spec.InstanceId, "workflow.start", "environment", spec.EnvironmentId, 1, 1, "synthetic_fixture", now, spec.RequestId);
    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken ct, NpgsqlTransaction? transaction = null)
    { await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 3 }; await command.ExecuteNonQueryAsync(ct); }
    private static async Task<long> Scalar(NpgsqlConnection connection, string sql, CancellationToken ct, params (string Name, object Value)[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 2 };
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kordra.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Product root missing.");
    }
    private static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
}

public sealed class WorkflowWakeFaultDatabaseFactAttribute : FactAttribute
{
    public WorkflowWakeFaultDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KORDRA_WFL_WAKE_FAULT_FIXTURE_MANIFEST")))
            Skip = "Requires exact fault SQL/schema/native producer/commit-response instrumentation approval; no DB or listener started.";
    }
}
