using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kordra.Api;
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

public sealed class WorkflowWakeReceiveFaultDatabaseTests
{
    public static readonly Guid CaseId = Guid.Parse("c9dbfca5-f8ad-47f5-b5e2-57993b1a27ef");
    private static string Schema => "t12_" + CaseId.ToString("N");
    private static string DirectoryPath => Path.Combine(Root(), ".amr-local", "t12-remaining-design", "t12-2d-wake-receive-fault-schema-review");

    [Fact]
    public void ExportReceiveFaultSqlMetadataWithoutStartingHostsOrOpeningConnections()
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
        using var listener = WorkflowWakeFixtureProfile.Builder("Host=127.0.0.1;Port=1;Database=not_opened;Username=not_opened;Timeout=1",
            Schema, new(CaseId, ""), true).Build();
        WorkflowTransactionSchemaTests.ConfigureTransportMetadata(listener);
        Assert.Equal(nativeSql, WorkflowTransactionSchemaTests.StableNativeScript(
            Assert.IsAssignableFrom<IDatabase>(listener.Services.GetRequiredService<IMessageStore>()), Schema));
        var scripts = new Dictionary<string, string> {
            ["wfl.sql"] = workflow.Database.GenerateCreateScript(), ["aud.sql"] = audit.Database.GenerateCreateScript(),
            ["msg-owned.sql"] = OwnedMessagingSchema.TablesSql.Replace("msg.", Schema + ".", StringComparison.Ordinal),
            ["msg-native.sql"] = nativeSql, ["cleanup.sql"] = "DROP SCHEMA " + Schema + " CASCADE;\n"
        };
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        foreach (var sql in scripts.Values) { Assert.Contains(Schema, sql); Assert.DoesNotContain("wolverine_queues", sql); }
        if (Environment.GetEnvironmentVariable("KORDRA_WFL_WAKE_RECEIVE_FAULT_ARTIFACT_DIRECTORY") is not { } directory) return;
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

    [WorkflowWakeReceiveFaultDatabaseFact]
    public async Task LostReceiverCommitReplyPreservesAtomicInboxOwnerAuditAndResultWithoutReplay()
    {
        var scripts = ReadApprovedScripts();
        var locator = Environment.GetEnvironmentVariable("KORDRA_WFL_WAKE_RECEIVE_FAULT_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Approved private receive-fault locator required.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90)); var ct = timeout.Token;
        var state = new WorkflowWakeFixtureState(CaseId, locator);
        await using var setup = state.NewOwnedConnection(); await setup.OpenAsync(ct);
        Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM pg_namespace WHERE nspname=@schema", ct, ("schema", Schema)));
        using var producer = WorkflowWakeFixtureProfile.Builder(locator, Schema, state, false).Build();
        using var receiver = WorkflowWakeFixtureProfile.Builder(locator, Schema, state, true).Build();
        var created = false; var producerStarted = false; var receiverStarted = false;
        try
        {
            await using (var ddl = await setup.BeginTransactionAsync(ct))
            {
                await Execute(setup, "CREATE SCHEMA " + Schema, ct, ddl);
                foreach (var name in new[] { "wfl.sql", "aud.sql", "msg-owned.sql", "msg-native.sql" }) await Execute(setup, scripts[name], ct, ddl);
                await ddl.CommitAsync(ct); created = true;
            }
            var (spec, timer) = await WorkflowWakeDatabaseTests.SeedWaitingTimer(setup, ct, CaseId);
            var now = DateTimeOffset.UtcNow;
            var wake = new WorkflowWakeOperation(spec, timer.StepId, timer.TimerId, 1, 1, 1,
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, 3, now, now.AddSeconds(120));
            var schedulerAudit = Audit(spec, now); var completionAudit = Audit(spec, now);
            var requests = WorkflowWakePublication.CreateRegistry(new WorkflowWakeFixtureHandler.RefuseRequest());
            var input = WorkflowWakePublication.Prepare(spec, wake, requests, now);
            var results = WorkflowTransitionResultPublication.CreateRegistry(new WorkflowWakeFixtureHandler.RefuseResult());
            WorkflowTransactionOrigin? origin = null; CommitReplyFault? fault = null;
            var observedOutcome = new TaskCompletionSource<WorkflowCommitDisposition>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resultId = Guid.NewGuid();
            var expectedOutput = WorkflowTransitionResultPublication.PrepareTimerDue(wake, 6, resultId, results, now);
            await using var recovery = IsolatedWorkflowFixture.Recovery(setup, CaseId);
            var invocation = new WorkflowWakeFixtureInvocation(spec, wake, completionAudit, Guid.NewGuid(), Guid.NewGuid(), resultId) {
                CommitOutcomeObserved = outcome => { observedOutcome.TrySetResult(outcome); },
                BeforeCommitFault = async (connection, captured, observed) => {
                    Assert.Equal(wake.WakeOperationId, captured.OperationId); Assert.Equal(wake.DescriptorHash, captured.DescriptorHash);
                    Assert.Equal(WorkflowTransactionProbeStatus.Unresolved, (await IsolatedWorkflowFixture.Probe(captured, setup, observed)).Status);
                    Assert.Equal(WorkflowReceiptInspectionStatus.MatchingObserved, await recovery.InspectSchedulerAsync(spec, wake, input, schedulerAudit, observed));
                    Assert.Equal(WorkflowReceiptInspectionStatus.StillUnknown, await recovery.InspectCompletionAsync(spec, wake, input, schedulerAudit, expectedOutput, completionAudit, observed));
                    Assert.Equal(WorkflowReceiptInspectionStatus.StillUnknown, await recovery.ReconcileCompletionAsync(
                        captured, spec, wake, input, schedulerAudit, expectedOutput, completionAudit, observed));
                    foreach (var changedAudit in new[] {
                        completionAudit with { IntentId = Guid.NewGuid() },
                        completionAudit with { SafeReason = "synthetic_different_intent" } })
                        Assert.Equal(WorkflowReceiptInspectionStatus.Conflict, await recovery.ReconcileCompletionAsync(
                            captured, spec, wake, input, schedulerAudit, expectedOutput, changedAudit, observed));
                    var changedResult = WorkflowTransitionResultPublication.PrepareTimerDue(wake, 6, Guid.NewGuid(), results, now);
                    Assert.Equal(expectedOutput.PayloadHash, changedResult.PayloadHash);
                    Assert.Equal(WorkflowReceiptInspectionStatus.Conflict, await recovery.ReconcileCompletionAsync(
                        captured, spec, wake, input, schedulerAudit, changedResult, completionAudit, observed));
                    await using var candidateReader = IsolatedWorkflowFixture.CancellationRecovery(setup, CaseId);
                    Assert.False(await candidateReader.ObserveAbortedCompletionAsync(captured, spec, wake, input,
                        schedulerAudit, expectedOutput, completionAudit, observed));
                    Assert.Equal(5L, await Scalar(setup, "SELECT version FROM " + Schema + ".instances WHERE instance_id=@id", observed, ("id", spec.InstanceId)));
                    Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".aud_intents WHERE intent_id=@id", observed, ("id", completionAudit.IntentId)));
                    Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".logical_receipts WHERE operation_id=@id", observed, ("id", wake.WakeOperationId)));
                    Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".publication_receipts WHERE operation_id=@id AND type_id=@type", observed, ("id", wake.WakeOperationId), ("type", WorkflowTransitionResultPublication.TypeId)));
                    Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".wolverine_incoming_envelopes WHERE id=@id AND status='Handled'", observed, ("id", wake.MessageId)));
                    origin = captured; fault = CommitReplyFault.Install(connection);
                }
            };
            state.Add(invocation);
            producerStarted = true; await producer.StartAsync(ct);
            await using (var connection = state.NewOwnedConnection())
            {
                await connection.OpenAsync(ct); await using var transaction = await connection.BeginTransactionAsync(ct);
                await using var root = await IsolatedWorkflowFixture.CreateIsolatedWakeTestAsync(connection, transaction, CaseId,
                    new MessageContext(producer.Services.GetRequiredService<IWolverineRuntime>()), state.Gate, ct);
                await root.StageInitialTimerDispatchAsync(spec, wake, requests, 4, 1, Guid.NewGuid(), Guid.NewGuid(), now, ct);
                await root.StageAuditAsync(schedulerAudit, ct); await root.StageClockFloorAsync(now, ct);
                Assert.Equal(WorkflowCommitDisposition.Committed, await root.CommitAsync(ct)); await root.FlushConfirmedAsync();
            }
            await producer.StopAsync(ct); producerStarted = false;
            receiverStarted = true; await receiver.StartAsync(ct);
            var first = await Task.WhenAny(observedOutcome.Task, invocation.Completion.Task).WaitAsync(ct);
            if (first == invocation.Completion.Task && !observedOutcome.Task.IsCompleted) await invocation.Completion.Task;
            Assert.Equal(WorkflowCommitDisposition.Unknown, await observedOutcome.Task.WaitAsync(ct));
            await Assert.ThrowsAsync<InvalidOperationException>(() => invocation.Completion.Task.WaitAsync(ct));
            Assert.NotNull(fault); Assert.True(fault.Dropped); Assert.NotNull(origin);
            await receiver.StopAsync(ct); receiverStarted = false;
            using (var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                bounded.CancelAfter(TimeSpan.FromSeconds(2));
                while (true)
                {
                    var observation = await IsolatedWorkflowFixture.Probe(origin, setup, bounded.Token);
                    Assert.Equal(WorkflowTransactionProbeStatus.Committed, observation.Status);
                    if (observation.OriginalBackendPresent == false) break;
                    await Task.Delay(20, bounded.Token);
                }
            }
            Assert.Equal(WorkflowReceiptInspectionStatus.MatchingObserved, await recovery.InspectCompletionAsync(spec, wake, input, schedulerAudit, expectedOutput, completionAudit, ct));
            Assert.Equal(WorkflowReceiptInspectionStatus.MatchingObserved,
                await recovery.ReconcileCompletionAsync(origin, spec, wake, input, schedulerAudit, expectedOutput, completionAudit, ct));
            await using (var candidateReader = IsolatedWorkflowFixture.CancellationRecovery(setup, CaseId))
                Assert.False(await candidateReader.ObserveAbortedCompletionAsync(origin, spec, wake, input,
                    schedulerAudit, expectedOutput, completionAudit, ct));
            Assert.Equal(WorkflowReceiptInspectionStatus.Conflict,
                await recovery.ReconcileSchedulerAsync(origin, spec, wake, input, schedulerAudit, ct));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var denied = await state.Gate.AcquireReadAsync(ct); });
            Assert.Equal(1, invocation.HandlerCalls);
            Assert.Equal(6L, await Scalar(setup, "SELECT version FROM " + Schema + ".instances WHERE instance_id=@id", ct, ("id", spec.InstanceId)));
            Assert.Equal(1L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".transitions WHERE operation_id=@id", ct, ("id", wake.WakeOperationId)));
            Assert.Equal(1L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".aud_intents WHERE intent_id=@id", ct, ("id", completionAudit.IntentId)));
            Assert.Equal(1L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".logical_receipts WHERE operation_id=@id", ct, ("id", wake.WakeOperationId)));
            Assert.Equal(1L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".publication_receipts WHERE operation_id=@id AND type_id=@type", ct, ("id", wake.WakeOperationId), ("type", WorkflowTransitionResultPublication.TypeId)));
            Assert.Equal(1L, await Scalar(setup, "SELECT count(*) FROM " + Schema + ".wolverine_incoming_envelopes WHERE id=@id AND status='Handled'", ct, ("id", wake.MessageId)));
            await using (var query = new NpgsqlCommand("SELECT body FROM " + Schema + ".wolverine_outgoing_envelopes WHERE id=@id UNION ALL SELECT body FROM " + Schema + ".wolverine_queue_workflow_results_v1 WHERE id=@id", setup))
            {
                query.Parameters.AddWithValue("id", resultId); await using var reader = await query.ExecuteReaderAsync(ct);
                var copies = 0;
                while (await reader.ReadAsync(ct))
                {
                    var envelope = EnvelopeSerializer.Deserialize(reader.GetFieldValue<byte[]>(0));
                    Assert.NotNull(envelope.Data); Assert.Equal(resultId, envelope.Id);
                    var output = new MessageEnvelopeCodec(results).Read(envelope.Data, MessageEnvelopeCodec.ContentType, CaseId, MessageScope.Tenant(CaseId), DateTimeOffset.UtcNow);
                    WorkflowTransitionResultPublication.RequireTimerDueMatch(output, wake, 6);
                    Assert.Equal(expectedOutput.PayloadHash, output.PayloadHash); copies++;
                }
                // Send/ACK overlap may retain the same physical envelope in outbox and target queue.
                // These are not two business publications; the original ID/hash and logical receipt stay exact.
                Assert.InRange(copies, 1, 2);
            }
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            if (producerStarted) await producer.StopAsync(cleanup.Token);
            if (receiverStarted) await receiver.StopAsync(cleanup.Token);
            if (created)
            {
                await Execute(setup, scripts["cleanup.sql"], cleanup.Token);
                Assert.Equal(0L, await Scalar(setup, "SELECT count(*) FROM pg_namespace WHERE nspname=@schema", cleanup.Token, ("schema", Schema)));
            }
        }
    }

    private static Dictionary<string, string> ReadApprovedScripts()
    {
        var file = Environment.GetEnvironmentVariable("KORDRA_WFL_WAKE_RECEIVE_FAULT_FIXTURE_MANIFEST")
            ?? throw new InvalidOperationException("Exact receive-fault fixture approval required.");
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

public sealed class WorkflowWakeReceiveFaultDatabaseFactAttribute : FactAttribute
{
    public WorkflowWakeReceiveFaultDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KORDRA_WFL_WAKE_RECEIVE_FAULT_FIXTURE_MANIFEST")))
            Skip = "Requires exact receive-fault SQL/schema/native host replacement/COMMIT-response loss approval; no DB or listener started.";
    }
}
