using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Api;
using Kordra.BuildingBlocks.Security;
using Kordra.Messaging.Tests;
using Kordra.Modules.AuditCompliance.Contracts;
using Kordra.Modules.AuditCompliance.Infrastructure;
using Kordra.Modules.Messaging.Contracts;
using Kordra.Modules.Messaging.Infrastructure.Serialization;
using Kordra.Modules.WorkflowOrchestration.Contracts;
using Kordra.Modules.WorkflowOrchestration.Domain;
using Kordra.Modules.WorkflowOrchestration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wolverine.Runtime;
using Xunit;

namespace Kordra.InfrastructureInventory.Bootstrap.Tests;

[Collection(LocalHostEnvironmentCollection.Name)]
public sealed class WorkflowAdmittedStartFaultDatabaseTests
{
    private static Guid CaseId => WorkflowStartOwnerFixture.CaseId;
    private static string Schema => WorkflowStartOwnerFixture.Schema;
    private static readonly string[] Cases = ["gate-before-allocation", "gate-after-allocation", "gate-before-commit", "same-key", "unknown"];

    [WorkflowAdmittedStartFaultFact]
    public async Task RealOwnersDenyLateGateClosureAndRetainSingleEffectAfterContentionOrUnknown()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(90)); var ct = budget.Token;
        var locator = Environment.GetEnvironmentVariable("KORDRA_WFL_ADMITTED_START_FAULT_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Exact approved start-fault locator required.");
        var settings = new NpgsqlConnectionStringBuilder(locator);
        Assert.Equal("127.0.0.1", settings.Host); Assert.Equal(5432, settings.Port);
        Assert.Equal(IsolatedWorkflowFixture.ExpectedDatabase, settings.Database); Assert.Equal("kordra_test", settings.Username);
        Assert.False(settings.Pooling); Assert.False(settings.Multiplexing);
        var scripts = WorkflowAdmittedStartDatabaseTests.Scripts();
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Environment.GetEnvironmentVariable("KORDRA_WFL_ADMITTED_START_FAULT_MANIFEST")!));
        Assert.Equal("approved_for_execution", manifest.RootElement.GetProperty("status").GetString());
        Assert.Equal(CaseId, manifest.RootElement.GetProperty("caseId").GetGuid());
        Assert.Equal(Cases, manifest.RootElement.GetProperty("cases").EnumerateArray().Select(x => x.GetString()!).ToArray());
        foreach (var (name, sql) in scripts)
            Assert.Equal(manifest.RootElement.GetProperty("sha256").GetProperty(name).GetString(), Hash(Encoding.UTF8.GetBytes(sql)));
        foreach (var scenario in Cases)
        {
            await using var setup = IsolatedWorkflowFixture.Connection(locator, CaseId); await setup.OpenAsync(ct);
            Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM pg_namespace WHERE nspname=@schema", ct, ("schema", Schema)));
            using var host = WorkflowTransactionSchemaTests.BuildNativeProfile(locator, Schema);
            WorkflowStartOwnerFixture? owners = null; var created = false; var started = false;
            try
            {
                await using (var ddl = await setup.BeginTransactionAsync(ct))
                {
                    await Execute(setup, "CREATE SCHEMA " + Schema, ct, ddl);
                    foreach (var (name, sql) in scripts.Where(x => x.Key != "cleanup.sql")) await Execute(setup, sql, ct, ddl);
                    await ddl.CommitAsync(ct); created = true;
                }
                var gate = new FairAsyncReaderWriterAdmissionGate();
                owners = await WorkflowStartOwnerFixture.SeedAsync(locator, gate, ct);
                await host.StartAsync(ct); started = true;
                var runtime = host.Services.GetRequiredService<IWolverineRuntime>();
                var request = Guid.NewGuid(); var invocation = Guid.NewGuid(); var deadline = DateTimeOffset.UtcNow.AddMinutes(3);
                await using var admission = await gate.AcquireReadAsync(ct);
                var original = await owners.Reader.ReadUnderAdmissionAsync(owners.Session, owners.Selection, request, invocation, ct);
                Assert.NotNull(original);
                var now = DateTimeOffset.UtcNow;
                var allocated = 0;
                WorkflowSpecification Allocate()
                {
                    allocated++;
                    return new(CaseId, CaseId, Guid.NewGuid(), request, owners.ProjectId, owners.ApplicationId,
                        owners.EnvironmentId, owners.TargetId, owners.ReleaseId, 1, owners.Selection.ReleaseHash,
                        WorkflowEnvironment.Test, now, deadline, now.AddSeconds(30));
                }
                if (scenario.StartsWith("gate-", StringComparison.Ordinal))
                {
                    var boundary = Array.IndexOf(Cases, scenario) + 1; var reads = 0;
                    var reader = owners.ObserveEligibility(() => { if (++reads == boundary) gate.Close(); });
                    await using var connection = IsolatedWorkflowFixture.Connection(locator, CaseId); await connection.OpenAsync(ct);
                    await using var transaction = await connection.BeginTransactionAsync(ct);
                    await using var root = await IsolatedWorkflowFixture.CreateIsolatedStartTestAsync(connection, transaction,
                        CaseId, new MessageContext(runtime), gate, ct);
                    WorkflowSpecification? specification = null;
                    Task<WorkflowStoredResult> Stage() => root.StageAdmittedStartAsync(original, reader, owners.Session,
                        deadline, scenario, () => specification = Allocate(), now, ct);
                    if (boundary < 3) await Assert.ThrowsAsync<InvalidOperationException>(Stage);
                    else
                    {
                        Assert.False((await Stage()).Duplicate); Assert.NotNull(specification);
                        await Pair(root, connection, transaction, specification, original, scenario, now, ct);
                        Assert.Equal(WorkflowCommitDisposition.NoCommitAttempt, await root.CommitAsync(ct));
                    }
                    Assert.Equal(boundary, reads); Assert.True(gate.IsClosed);
                    Assert.Equal(boundary == 1 ? 0 : 1, allocated);
                    await AssertCounts(setup, 0, ct);
                }
                else if (scenario == "same-key")
                {
                    await using var first = IsolatedWorkflowFixture.Connection(locator, CaseId); await first.OpenAsync(ct);
                    await using var second = IsolatedWorkflowFixture.Connection(locator, CaseId); await second.OpenAsync(ct);
                    await using var transactionA = await first.BeginTransactionAsync(ct);
                    await using var transactionB = await second.BeginTransactionAsync(ct);
                    await using var winner = await IsolatedWorkflowFixture.CreateIsolatedStartTestAsync(first, transactionA,
                        CaseId, new MessageContext(runtime), gate, ct);
                    WorkflowSpecification? specification = null;
                    Assert.False((await winner.StageAdmittedStartAsync(original, owners.Reader, owners.Session, deadline,
                        scenario, () => specification = Allocate(), now, ct)).Duplicate);
                    Assert.NotNull(specification);
                    await Pair(winner, first, transactionA, specification, original, scenario, now, ct);
                    await using var loser = await IsolatedWorkflowFixture.CreateIsolatedStartTestAsync(second, transactionB,
                        CaseId, new MessageContext(runtime), gate, ct);
                    var allocation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var competing = loser.StageAdmittedStartAsync(original, owners.Reader, owners.Session, deadline,
                        scenario, () => { var value = Allocate(); allocation.SetResult(); return value; }, now, ct);
                    await allocation.Task.WaitAsync(ct);
                    await WaitForOwnedLockWait(setup, second.ProcessID, competing, ct);
                    Assert.Equal(WorkflowCommitDisposition.Committed, await winner.CommitAsync(ct));
                    var failure = await Assert.ThrowsAsync<DbUpdateException>(() => competing);
                    Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
                    Assert.False(gate.IsClosed); Assert.Equal(2, allocated);
                    // The loser root is terminal/rolled back. Retry is explicit, fresh and still current; it never replays the effect.
                    await using var retryConnection = IsolatedWorkflowFixture.Connection(locator, CaseId); await retryConnection.OpenAsync(ct);
                    await using var retryTransaction = await retryConnection.BeginTransactionAsync(ct);
                    await using var retry = await IsolatedWorkflowFixture.CreateIsolatedStartTestAsync(retryConnection, retryTransaction,
                        CaseId, new MessageContext(runtime), gate, ct);
                    var result = await retry.StageAdmittedStartAsync(original, owners.Reader, owners.Session, deadline, scenario,
                        () => throw new InvalidOperationException("Contended retry allocated again."), DateTimeOffset.UtcNow, ct);
                    Assert.True(result.Duplicate); Assert.Equal(specification.InstanceId, result.InstanceId);
                    await AssertCounts(setup, 1, ct);
                }
                else
                {
                    WorkflowSpecification? specification = null;
                    string xid; int pid; DateTime server;
                    AuditIntent audit; VerifiedMessageEnvelope publication;
                    await using (var connection = IsolatedWorkflowFixture.Connection(locator, CaseId))
                    {
                        await connection.OpenAsync(ct); pid = connection.ProcessID;
                        await using var transaction = await connection.BeginTransactionAsync(ct);
                        await using var root = await IsolatedWorkflowFixture.CreateIsolatedStartTestAsync(connection, transaction,
                            CaseId, new MessageContext(runtime), gate, ct);
                        Assert.False((await root.StageAdmittedStartAsync(original, owners.Reader, owners.Session, deadline,
                            scenario, () => specification = Allocate(), now, ct)).Duplicate);
                        Assert.NotNull(specification);
                        (audit, publication) = await Pair(root, connection, transaction, specification, original, scenario, now, ct);
                        await using (var origin = new NpgsqlCommand("SELECT pg_current_xact_id()::text, pg_postmaster_start_time()", connection, transaction))
                        await using (var originReader = await origin.ExecuteReaderAsync(ct))
                        { Assert.True(await originReader.ReadAsync(ct)); xid = originReader.GetString(0); server = originReader.GetDateTime(1); }
                        Assert.Equal("in progress", await Scalar(setup, "SELECT pg_xact_status(@xid::xid8)::text", ct, ("xid", xid)));
                        await AssertCounts(setup, 0, ct);
                        var fault = CommitReplyFault.Install(connection);
                        Assert.Equal(WorkflowCommitDisposition.Unknown, await root.CommitAsync(ct)); Assert.True(fault.Dropped);
                        Assert.True(gate.IsClosed);
                        await Assert.ThrowsAsync<InvalidOperationException>(() => root.FlushConfirmedAsync());
                    }
                    using (var terminal = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        terminal.CancelAfter(TimeSpan.FromSeconds(3));
                        while (await Scalar(setup, "SELECT count(*) FROM pg_stat_activity WHERE pid=@pid", terminal.Token, ("pid", pid)) is 1L)
                            await Task.Delay(10, terminal.Token);
                    }
                    Assert.Equal(server, await Scalar(setup, "SELECT pg_postmaster_start_time()", ct));
                    Assert.Equal("committed", await Scalar(setup, "SELECT pg_xact_status(@xid::xid8)::text", ct, ("xid", xid)));
                    await using var inspector = IsolatedWorkflowFixture.WorkflowReceipt(setup, CaseId);
                    var expected = new WorkflowReceiptExpectation(new(CaseId, CaseId, specification!.InstanceId), request, specification.SpecificationHash, 1);
                    Assert.Equal(WorkflowReceiptInspectionStatus.MatchingObserved, await inspector.InspectAsync(expected, ct));
                    await AssertExactRoot(setup, specification, original.BindCommand(deadline, scenario), audit, publication, ct);
                    await AssertCounts(setup, 1, ct);
                    Assert.Equal(1, allocated); Assert.True(gate.IsClosed);
                    // Observation is evidence only. Never reopen the gate, retry the Unknown mutation, or claim operator reconciliation.
                }
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                if (owners is not null) await owners.DisposeAsync();
                if (started) await host.StopAsync(cleanup.Token);
                if (created) await Execute(setup, scripts["cleanup.sql"], cleanup.Token);
                Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM pg_namespace WHERE nspname=@schema", cleanup.Token, ("schema", Schema)));
            }
        }
    }

    private static async Task<(AuditIntent, VerifiedMessageEnvelope)> Pair(WorkflowRootTransaction root, NpgsqlConnection connection,
        NpgsqlTransaction transaction, WorkflowSpecification spec, WorkflowStartAdmission admitted, string key, DateTimeOffset now, CancellationToken ct)
    {
        var audit = admitted.CreateMutationAudit(spec, admitted.BindCommand(spec.DeadlineUtc, key), Guid.NewGuid());
        await root.StageAuditAsync(audit, ct); await root.StageClockFloorAsync(now, ct);
        await using var db = IsolatedWorkflowFixture.CreateStartFixture(connection, CaseId); await db.Database.UseTransactionAsync(transaction, ct);
        var step = await db.Set<WorkflowStepRecord>().Where(x => x.InstanceId == spec.InstanceId && x.Ordinal == 0).Select(x => x.StepId).SingleAsync(ct);
        var registry = WorkflowWakePublication.CreateRegistry(new Refuse()); var codec = new MessageEnvelopeCodec(registry);
        var payload = new WorkflowTransitionRequestedV1(spec.InstanceId, step, 1, 1, spec.RequestId,
            spec.SpecificationHash, WorkflowSpecification.TemplateHash, WorkflowTransitionKind.Checkpoint, null, null);
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes)) new WorkflowTransitionRequestedCodec().Write(writer, payload);
        var binding = new MessageBinding(new(CaseId, MessageScope.Tenant(CaseId), spec.RequestId, step, 1,
            WorkflowWakePublication.TypeId), 1, new("workflow", spec.InstanceId), now, now.AddSeconds(120));
        var wire = codec.Write(binding, new(Guid.NewGuid(), spec.RequestId, Guid.NewGuid()), bytes.ToArray());
        var envelope = codec.Read(wire, MessageEnvelopeCodec.ContentType, CaseId, MessageScope.Tenant(CaseId), now);
        await root.StagePublicationAsync(registry, envelope, now, ct); return (audit, envelope);
    }
    private static async Task WaitForOwnedLockWait(NpgsqlConnection setup, int pid, Task competing, CancellationToken ct)
    {
        var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
        while (Stopwatch.GetTimestamp() < until)
        {
            if (await Scalar(setup, "SELECT wait_event_type='Lock' FROM pg_stat_activity WHERE pid=@pid", ct, ("pid", pid)) is true) return;
            if (competing.IsCompleted) { await competing; throw new InvalidOperationException("Competing start did not reach its owned lock wait."); }
            await Task.Delay(5, ct);
        }
        throw new TimeoutException("Owned competing start lock wait not observed within one second.");
    }
    private static async Task AssertExactRoot(NpgsqlConnection setup, WorkflowSpecification spec, WorkflowStartCommand command,
        AuditIntent audit, VerifiedMessageEnvelope publication, CancellationToken ct)
    {
        await using var query = new NpgsqlCommand($"""
            SELECT count(*) FROM {Schema}.instances i
            JOIN {Schema}.request_receipts r ON r.instance_id=i.instance_id AND r.request_id=i.request_id
            JOIN {Schema}.start_receipts s ON s.request_id=i.request_id AND s.installation_id=i.installation_id AND s.tenant_id=i.tenant_id
            JOIN {Schema}.publication_receipts p ON p.operation_id=i.request_id AND p.installation_id=i.installation_id AND p.tenant_key=i.tenant_id
            JOIN {Schema}.aud_intents a ON a.intent_id=@intent
            WHERE i.installation_id=@installation AND i.tenant_id=@installation AND i.instance_id=@instance
            AND i.specification_hash=@spec AND i.specification=@canonical AND i.version=1 AND i.state=1
            AND r.content_hash=@spec AND s.actor_id=@actor AND s.scoped_key_hash=@key AND s.business_hash=@business
            AND p.binding_hash=@publication AND a.canonical_intent_hash=@audit
            """, setup);
        foreach (var (name, value) in new (string, object)[] { ("installation", CaseId), ("instance", spec.InstanceId),
            ("spec", spec.SpecificationHash), ("canonical", spec.WriteCanonical()), ("actor", command.ActorId),
            ("key", command.ScopedKeyHash), ("business", command.BusinessHash), ("intent", audit.IntentId),
            ("audit", Convert.FromHexString(AuditIntentCanonicalizer.ComputeHash(audit))), ("publication", publication.PayloadHash) })
            query.Parameters.AddWithValue(name, value);
        Assert.Equal(1L, await query.ExecuteScalarAsync(ct));
    }
    private static async Task AssertCounts(NpgsqlConnection setup, long expected, CancellationToken ct)
    {
        foreach (var table in new[] { "instances", "request_receipts", "start_receipts", "aud_intents", "publication_receipts", "clock_floor", "wolverine_outgoing_envelopes" })
            Assert.Equal(expected, await Scalar(setup, $"SELECT count(*) FROM {Schema}.{table}", ct));
    }
    private static async Task<object?> Scalar(NpgsqlConnection connection, string sql, CancellationToken ct, params (string Name, object Value)[] args)
    { await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 2 }; foreach (var (name, value) in args) command.Parameters.AddWithValue(name, value); return await command.ExecuteScalarAsync(ct); }
    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken ct, NpgsqlTransaction? transaction = null)
    { await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 3 }; await command.ExecuteNonQueryAsync(ct); }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private sealed class Refuse : IMessageConsumer<WorkflowTransitionRequestedV1>
    { public ValueTask HandleAsync(WorkflowTransitionRequestedV1 message, CancellationToken ct) => throw new InvalidOperationException("No fixture listener."); }
}

public sealed class WorkflowAdmittedStartFaultFactAttribute : FactAttribute
{
    public WorkflowAdmittedStartFaultFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KORDRA_WFL_ADMITTED_START_FAULT_MANIFEST")))
            Skip = "Requires exact approval for five isolated start-fault fixture cycles.";
    }
}
