using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Persistence;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Weasel.Core;
using Weasel.Core.Migrations;
using Wolverine;
using Wolverine.Persistence.Durability;
using Wolverine.Runtime;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

public sealed class WorkflowWakeDatabaseTests
{
    public static readonly Guid CaseId = Guid.Parse("bc6451a6-0f76-42a2-ae9e-177f422a7dd1");
    private static string Schema => "t12_" + CaseId.ToString("N");
    private static string DirectoryPath => Path.Combine(Root(), ".amr-local", "t12-remaining-design", "t12-2d-wake-root-schema-review");

    [Fact]
    public void ExportExactWakeRootFixtureWithoutOpeningConnectionsOrStartingHost()
    {
        var directory = Environment.GetEnvironmentVariable("KORDRA_WFL_WAKE_ROOT_ARTIFACT_DIRECTORY");
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Database=kordra_test;Username=kordra_test;Password=SYNTHETIC");
        using var workflow = WorkflowDbContext.CreateFixture(connection, CaseId);
        using var audit = AuditDbContextFactory.CreateIsolatedTestContext(connection, CaseId);
        using var host = WorkflowTransactionSchemaTests.BuildNativeProfile(
            "Host=127.0.0.1;Port=1;Database=not_opened;Username=not_opened;Timeout=1", Schema);
        WorkflowTransactionSchemaTests.ConfigureTransportMetadata(host);
        var native = Assert.IsAssignableFrom<IDatabase>(host.Services.GetRequiredService<IMessageStore>());
        var files = new Dictionary<string, string> {
            ["wfl.sql"] = workflow.Database.GenerateCreateScript(),
            ["wake-expansion.sql"] = File.ReadAllText(Path.Combine(Root(),
                "src/Modules/WorkflowOrchestration/Infrastructure/Migrations/wfl-v2-wake.sql")).Replace("wfl.", Schema + ".", StringComparison.Ordinal),
            ["aud.sql"] = audit.Database.GenerateCreateScript(),
            ["msg-owned.sql"] = OwnedMessagingSchema.TablesSql.Replace("msg.", Schema + ".", StringComparison.Ordinal),
            ["msg-native.sql"] = WorkflowTransactionSchemaTests.StableNativeScript(native, Schema),
            ["cleanup.sql"] = "DROP SCHEMA " + Schema + " CASCADE;\n"
        };
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        foreach (var sql in files.Values)
        { Assert.Contains(Schema, sql); Assert.DoesNotContain("wolverine_queues", sql); Assert.DoesNotContain("SYNTHETIC", sql); }
        if (directory is null) return;
        Assert.Equal(DirectoryPath, Path.GetFullPath(directory)); Directory.CreateDirectory(directory);
        foreach (var (name, sql) in files)
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path)) Assert.Equal(sql, File.ReadAllText(path)); else File.WriteAllText(path, sql);
        }
        var manifest = JsonSerializer.Serialize(new { status = "proposal_not_operation_authorization", caseId = CaseId, schema = Schema,
            sha256 = files.ToDictionary(x => x.Key, x => Hash(Encoding.UTF8.GetBytes(x.Value))) }, new JsonSerializerOptions { WriteIndented = true });
        var manifestPath = Path.Combine(directory, "manifest.json");
        if (File.Exists(manifestPath)) Assert.Equal(manifest, File.ReadAllText(manifestPath)); else File.WriteAllText(manifestPath, manifest);
    }

    [WorkflowWakeDatabaseFact]
    public async Task ActualTimerWakeAuditAndOutboxAreAtomicAndDuplicateCannotRepublish()
    {
        var scripts = ReadApprovedScripts();
        var locator = Environment.GetEnvironmentVariable("KORDRA_WFL_WAKE_ROOT_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Approved private test locator required.");
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60)); var ct = budget.Token;
        await using var setup = Connection(locator); await setup.OpenAsync(ct);
        Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM pg_namespace WHERE nspname=@schema", ct, ("schema", Schema)));
        using var host = WorkflowTransactionSchemaTests.BuildNativeProfile(locator, Schema);
        var created = false; var started = false;
        try
        {
            await using (var ddl = await setup.BeginTransactionAsync(ct))
            {
                await Execute(setup, "CREATE SCHEMA " + Schema, ct, ddl);
                foreach (var name in new[] { "wfl.sql", "aud.sql", "msg-owned.sql", "msg-native.sql" })
                    await Execute(setup, scripts[name], ct, ddl);
                await ddl.CommitAsync(ct); created = true;
            }
            var (spec, schedule) = await SeedWaitingTimer(setup, ct);
            // Upgrade a real revision1 fixture with owner rows already present; do not recreate or rewrite them.
            var before = await Scalar(setup, "SELECT version FROM " + Schema + ".instances WHERE instance_id=@id", ct, ("id", spec.InstanceId));
            await Execute(setup, scripts["wake-expansion.sql"], ct);
            Assert.Equal(before, await Scalar(setup, "SELECT version FROM " + Schema + ".instances WHERE instance_id=@id", ct, ("id", spec.InstanceId)));
            started = true; await host.StartAsync(ct);
            var runtime = host.Services.GetRequiredService<IWolverineRuntime>();
            var gate = new FairAsyncReaderWriterAdmissionGate();
            var registry = WorkflowWakePublication.CreateRegistry(new RefuseConsumer());
            var now = DateTimeOffset.UtcNow;
            var wake = new WorkflowWakeOperation(spec, schedule.StepId, schedule.TimerId, 1, 1, 1,
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, 3, now, now.AddSeconds(120));
            var worker = Guid.NewGuid(); var boot = Guid.NewGuid();
            var expected = WorkflowWakePublication.Prepare(spec, wake, registry, now);

            // Missing AUD/floor and explicit rollback all preserve the original timer/version and leave no paired records.
            foreach (var mode in new[] { "missing-audit", "missing-floor", "rollback", "wrong-floor" })
            {
                await using var connection = Connection(locator); await connection.OpenAsync(ct);
                await using var transaction = await connection.BeginTransactionAsync(ct);
                await using var root = await IsolatedWorkflowFixture.CreateIsolatedWakeTestAsync(connection, transaction,
                    CaseId, new MessageContext(runtime), gate, ct);
                Assert.False((await root.StageInitialTimerDispatchAsync(spec, wake, registry, 4, 1, worker, boot, now, ct)).Duplicate);
                await AssertUnchanged();
                await Assert.ThrowsAsync<InvalidOperationException>(() => root.FlushConfirmedAsync());
                if (mode != "missing-audit") await root.StageAuditAsync(Audit(spec, now), ct);
                if (mode == "wrong-floor")
                    await Assert.ThrowsAsync<InvalidOperationException>(() => root.StageClockFloorAsync(now.AddTicks(1), ct));
                else if (mode == "rollback") await root.RollbackAsync();
                else Assert.Equal(WorkflowCommitDisposition.NoCommitAttempt, await root.CommitAsync(ct));
                await AssertUnchanged();
            }

            await using (var connection = Connection(locator))
            {
                await connection.OpenAsync(ct); await using var transaction = await connection.BeginTransactionAsync(ct);
                await using var root = await IsolatedWorkflowFixture.CreateIsolatedWakeTestAsync(connection, transaction,
                    CaseId, new MessageContext(runtime), gate, ct);
                await Assert.ThrowsAsync<ArgumentException>(() => root.StageInitialTimerDispatchAsync(spec, wake, registry,
                    3, 1, worker, boot, now, ct));
                await AssertUnchanged();
            }

            foreach (var failure in new[] { "wrong-tenant", "wrong-step-version", "stale-timer", "cancelled", "unknown-custody" })
            {
                await using var connection = Connection(locator); await connection.OpenAsync(ct);
                await using var transaction = await connection.BeginTransactionAsync(ct);
                await using var root = await IsolatedWorkflowFixture.CreateIsolatedWakeTestAsync(connection, transaction,
                    CaseId, new MessageContext(runtime), gate, ct);
                var candidateSpec = failure == "wrong-tenant" ? new WorkflowSpecification(spec.InstallationId,
                    Guid.NewGuid(), spec.InstanceId, spec.RequestId, spec.ProjectId, spec.ApplicationId, spec.EnvironmentId,
                    spec.TargetId, spec.ReleaseId, spec.ReleaseRevision, spec.ReleaseHash, spec.Environment,
                    spec.CreatedAtUtc, spec.DeadlineUtc, spec.DueAtUtc) : spec;
                var candidate = new WorkflowWakeOperation(candidateSpec, wake.StepId, wake.TimerId, 1, 1, 1,
                    wake.SchedulerOperationId, wake.WakeOperationId, wake.MessageId, 5,
                    failure == "wrong-step-version" ? 99 : 3, now, wake.DeadlineUtc);
                if (failure == "cancelled")
                {
                    await using var owner = IsolatedWorkflowFixture.CreateWakeFixture(connection, CaseId);
                    await owner.Database.UseTransactionAsync(transaction, ct);
                    await new WorkflowStore(owner).StageTransitionAsync(new(CaseId, CaseId, spec.InstanceId), wake.StepId,
                        Guid.NewGuid(), spec.SpecificationHash, 4, WorkflowState.CancelRequested, now, ct);
                }
                if (failure == "unknown-custody")
                {
                    await using var fault = new NpgsqlCommand("UPDATE " + Schema +
                        ".operation_custody SET phase=2 WHERE installation_id=@case AND tenant_id=@case AND instance_id=@instance AND step_id=@step", connection, transaction);
                    fault.Parameters.AddWithValue("case", CaseId); fault.Parameters.AddWithValue("instance", spec.InstanceId);
                    fault.Parameters.AddWithValue("step", wake.StepId); Assert.Equal(1, await fault.ExecuteNonQueryAsync(ct));
                }
                await Assert.ThrowsAsync<InvalidOperationException>(() => root.StageInitialTimerDispatchAsync(candidateSpec,
                    candidate, registry, 4, failure == "stale-timer" ? 2 : 1, worker, boot, now, ct));
                await AssertUnchanged();
            }

            var intent = Audit(spec, now);
            await using (var connection = Connection(locator))
            {
                await connection.OpenAsync(ct); await using var transaction = await connection.BeginTransactionAsync(ct);
                await using var root = await IsolatedWorkflowFixture.CreateIsolatedWakeTestAsync(connection, transaction,
                    CaseId, new MessageContext(runtime), gate, ct);
                var result = await root.StageInitialTimerDispatchAsync(spec, wake, registry, 4, 1, worker, boot, now, ct);
                Assert.False(result.Duplicate); Assert.Equal(5, result.WorkflowVersion);
                await root.StageAuditAsync(intent, ct); await root.StageClockFloorAsync(now, ct);
                await AssertUnchanged();
                Assert.Equal(WorkflowCommitDisposition.Committed, await root.CommitAsync(ct));
                Assert.Equal(1L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".wake_operations", ct));
                Assert.Equal(1L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".publication_receipts WHERE operation_id=@id", ct, ("id", wake.WakeOperationId)));
                Assert.Equal(1L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".aud_intents WHERE intent_id=@id", ct, ("id", intent.IntentId)));
                await using var wireCommand = new NpgsqlCommand("SELECT body FROM " + Schema + ".wolverine_outgoing_envelopes WHERE id=@id", setup);
                wireCommand.Parameters.AddWithValue("id", wake.MessageId);
                var wire = Assert.IsType<byte[]>(await wireCommand.ExecuteScalarAsync(ct));
                var native = Wolverine.Runtime.Serialization.EnvelopeSerializer.Deserialize(wire);
                Assert.Equal(wake.MessageId, native.Id); Assert.NotNull(native.Data);
                var read = new MessageEnvelopeCodec(registry).Read(native.Data, MessageEnvelopeCodec.ContentType, CaseId,
                    MessageScope.Tenant(CaseId), DateTimeOffset.UtcNow);
                Assert.Equal(expected.PayloadHash, read.PayloadHash); Assert.Equal(expected.Delivery, read.Delivery);
                Assert.Equal(expected.Binding, read.Binding);
                await root.FlushConfirmedAsync();
            }
            foreach (var changed in new[] { false, true })
            {
                var retry = changed ? new WorkflowWakeOperation(spec, wake.StepId, wake.TimerId, 1, 1, 1,
                    wake.SchedulerOperationId, wake.WakeOperationId, Guid.NewGuid(), 5, 3, now, now.AddSeconds(120)) : wake;
                await using var connection = Connection(locator); await connection.OpenAsync(ct);
                await using var transaction = await connection.BeginTransactionAsync(ct);
                await using var root = await IsolatedWorkflowFixture.CreateIsolatedWakeTestAsync(connection, transaction,
                    CaseId, new MessageContext(runtime), gate, ct);
                if (changed) await Assert.ThrowsAsync<InvalidOperationException>(() => root.StageInitialTimerDispatchAsync(spec,
                    retry, registry, 4, 1, worker, boot, now, ct));
                else Assert.True((await root.StageInitialTimerDispatchAsync(spec, retry, registry, 4, 1, worker, boot, now, ct)).Duplicate);
                await Assert.ThrowsAsync<InvalidOperationException>(() => root.FlushConfirmedAsync());
            }
            Assert.Equal(1L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".wake_operations", ct));
            Assert.Equal(1L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".publication_receipts WHERE operation_id=@id", ct, ("id", wake.WakeOperationId)));
            Assert.Equal(5L, await Scalar(setup, "SELECT version FROM " + Schema + ".instances WHERE instance_id=@id", ct, ("id", spec.InstanceId)));
            Assert.Equal(2L, await Scalar(setup, "SELECT status::bigint FROM " + Schema + ".timers WHERE timer_id=@id", ct, ("id", wake.TimerId)));
            using var stillOpen = await gate.AcquireReadAsync(ct);

            async Task AssertUnchanged()
            {
                Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".wake_operations", ct));
                Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".publication_receipts", ct));
                Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".aud_intents", ct));
                Assert.Equal(4L, await Scalar(setup, "SELECT version FROM " + Schema + ".instances WHERE instance_id=@id", ct, ("id", spec.InstanceId)));
                Assert.Equal(1L, await Scalar(setup, "SELECT status::bigint FROM " + Schema + ".timers WHERE timer_id=@id", ct, ("id", wake.TimerId)));
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

    /// <param name="subMicrosecondTicks">0, or 1–9 canonical 100ns ticks that PostgreSQL timestamp columns cannot store.</param>
    internal static async Task<(WorkflowSpecification, (Guid StepId, Guid TimerId))> SeedWaitingTimer(NpgsqlConnection connection,
        CancellationToken ct, Guid? caseId = null, int subMicrosecondTicks = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(subMicrosecondTicks);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(subMicrosecondTicks, 9);
        var fixtureCase = caseId ?? CaseId;
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var context = IsolatedWorkflowFixture.CreateFixture(connection, fixtureCase);
        await context.Database.UseTransactionAsync(transaction, ct);
        var start = DateTimeOffset.UtcNow.AddMinutes(-10);
        // Created, deadline and due inherit the same seven-digit fraction from this origin.
        if (subMicrosecondTicks != 0) start = new(start.UtcTicks - start.UtcTicks % 10 + subMicrosecondTicks, TimeSpan.Zero);
        var spec = new WorkflowSpecification(fixtureCase, fixtureCase, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('a', 64), WorkflowEnvironment.Test,
            start, start.AddHours(1), start.AddMinutes(1));
        var store = new WorkflowStore(context); await store.StageCreationAsync(spec, spec.CreatedAtUtc, ct);
        var identity = new WorkflowIdentity(fixtureCase, fixtureCase, spec.InstanceId); var worker = Guid.NewGuid(); var boot = Guid.NewGuid();
        var first = await context.Set<WorkflowStepRecord>().SingleAsync(x => x.InstanceId == spec.InstanceId && x.Ordinal == 0, ct);
        await store.StageTransitionAsync(identity, first.StepId, Guid.NewGuid(), spec.SpecificationHash, 1, WorkflowState.Running, start, ct);
        var checkpointId = Guid.NewGuid();
        var custody = await store.StageInitialCustodyAsync(identity, first.StepId, checkpointId, spec.SpecificationHash, worker, boot, 1, start, ct);
        // Synthetic admitted checkpoint facts only; no production handler or provider is activated.
        first.State = (int)WorkflowStepState.Succeeded; first.Version++; custody.Phase = 3; custody.Version++;
        custody.LastCommitReceiptId = checkpointId;
        var instance = await context.Set<WorkflowInstanceRecord>().SingleAsync(x => x.InstanceId == spec.InstanceId, ct);
        instance.Version++;
        context.Add(new WorkflowTransitionRecord { InstallationId = fixtureCase, TenantId = fixtureCase, InstanceId = spec.InstanceId,
            StepId = first.StepId, OperationId = checkpointId, SpecificationHash = spec.SpecificationHash,
            ResultingState = (int)WorkflowState.Running, ResultingVersion = 3, RecordedAtUtc = start });
        await context.SaveChangesAsync(ct);
        var step = await context.Set<WorkflowStepRecord>().SingleAsync(x => x.InstanceId == spec.InstanceId && x.Ordinal == 1, ct);
        var operation = Guid.NewGuid();
        await store.StageInitialCustodyAsync(identity, step.StepId, operation, spec.SpecificationHash, worker, boot, 1, start, ct);
        var scheduled = await new WorkflowTimerStore(context).StageWaitingAsync(new(identity, step.StepId, operation,
            spec.SpecificationHash, 3, 2, new(worker, boot, 1, 1), start.AddSeconds(1)), ct);
        await transaction.CommitAsync(ct);
        return (spec, (step.StepId, scheduled.TimerId));
    }

    private static Dictionary<string, string> ReadApprovedScripts()
    {
        var path = Environment.GetEnvironmentVariable("KORDRA_WFL_WAKE_ROOT_FIXTURE_MANIFEST")
            ?? throw new InvalidOperationException("Exact wake fixture approval required.");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = manifest.RootElement;
        Assert.Equal("approved_for_execution", root.GetProperty("status").GetString());
        Assert.Equal(CaseId, root.GetProperty("caseId").GetGuid()); Assert.Equal(Schema, root.GetProperty("schema").GetString());
        var files = new Dictionary<string, string>();
        foreach (var property in root.GetProperty("sha256").EnumerateObject())
        {
            Assert.Contains(property.Name, new[] { "wfl.sql", "wake-expansion.sql", "aud.sql", "msg-owned.sql", "msg-native.sql", "cleanup.sql" });
            var bytes = File.ReadAllBytes(Path.Combine(DirectoryPath, property.Name));
            Assert.Equal(property.Value.GetString(), Hash(bytes)); files.Add(property.Name, Encoding.UTF8.GetString(bytes));
        }
        Assert.Equal(6, files.Count); Assert.Equal("DROP SCHEMA " + Schema + " CASCADE;\n", files["cleanup.sql"]);
        return files;
    }
    private static NpgsqlConnection Connection(string locator)
    {
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(locator) { Pooling = false, Multiplexing = false }.ConnectionString);
        _ = IsolatedWorkflowFixture.Validate(connection, CaseId); return connection;
    }
    private static AuditIntent Audit(WorkflowSpecification spec, DateTimeOffset now) => new(1, Guid.NewGuid(), Guid.NewGuid(),
        AuditIntentStage.Mutation, Guid.NewGuid(), CaseId, spec.InstanceId, "workflow.start", "environment", spec.EnvironmentId,
        1, 1, "synthetic_fixture", now, spec.RequestId);
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
    private sealed class RefuseConsumer : IMessageConsumer<WorkflowTransitionRequestedV1>
    {
        public ValueTask HandleAsync(WorkflowTransitionRequestedV1 message, CancellationToken ct)
            => throw new InvalidOperationException("Fixture schema cannot execute business messages.");
    }
}

public sealed class WorkflowWakeDatabaseFactAttribute : FactAttribute
{
    public WorkflowWakeDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KORDRA_WFL_WAKE_ROOT_FIXTURE_MANIFEST")))
            Skip = "Requires separately approved exact D2 schema/SQL, native producer host and cleanup; no IO performed.";
    }
}
